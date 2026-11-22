using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class ApplicationAccessService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly LogService _logService;
        private readonly TimeProvider _clock;
        private readonly OperatorSession _session;
        private readonly SemaphoreSlim _loginGate = new SemaphoreSlim(1);
        private int _failedLogins;
        private DateTimeOffset _lockedUntil;

        public ApplicationAccessService(IDbContextFactory<ServeXDbContext> contexts, LogService logService, TimeProvider clock = null, OperatorSession session = null)
        {
            _contexts = contexts;
            _logService = logService;
            _clock = clock ?? TimeProvider.System;
            _session = session;
        }

        public async Task<StartupFlow> GetStartupFlowAsync()
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var settings = await context.Settings
                        .AsNoTracking()
                        .OrderBy(setting => setting.Id)
                        .FirstOrDefaultAsync();

                    if (settings == null)
                    {
                        _logService.LogInfo("Startup flow resolved to setup");
                        return StartupFlow.Setup;
                    }

                    _logService.LogInfo("Startup flow resolved to login");
                    return StartupFlow.Login;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Startup flow check failed", exception);
                throw;
            }
        }

        public async Task SaveInitialSetupAsync(InitialSetupRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var normalizedUsername = request.AdminUsername.Trim();

            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    if (await context.Settings.AnyAsync())
                    {
                        throw new InvalidOperationException("ServeX has already been configured.");
                    }

                    if (await context.Users.AnyAsync(user => EF.Functions.Collate(user.Username, "NOCASE") == normalizedUsername))
                    {
                        throw new InvalidOperationException("That admin username is already in use.");
                    }

                    using (var transaction = await context.Database.BeginTransactionAsync())
                    {
                        context.Settings.Add(new AppSetting
                        {
                            RestaurantName = request.RestaurantName.Trim(),
                            Address = request.Address.Trim(),
                            Phone = request.Phone.Trim(),
                            TaxPercentage = decimal.Round(request.TaxPercentage, 2)
                        });

                        context.Users.Add(new User
                        {
                            Username = normalizedUsername,
                            Password = PasswordHash.Create(request.AdminPassword),
                            IsPasswordHashed = true
                        });

                        await context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }

                    _logService.LogInfo(string.Format("Initial setup saved for restaurant {0}", request.RestaurantName.Trim()));
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Initial setup save failed", exception);
                throw;
            }
        }

        public async Task CreateStaffAsync(string username, string password, UserRole role)
        {
            if (_session?.CanManageStaff != true)
                throw new InvalidOperationException("Sign in as an administrator to add staff.");
            username = username?.Trim() ?? string.Empty;
            if (username.Length is < 1 or > 50 || string.IsNullOrWhiteSpace(password) ||
                password.Length is < 8 or > 128 || !Enum.IsDefined(role))
                throw new ArgumentException("Enter a username, role, and a password between 8 and 128 characters.");

            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            if (await context.Users.AnyAsync(user => EF.Functions.Collate(user.Username, "NOCASE") == username))
                throw new InvalidOperationException("That username is already in use.");
            context.Users.Add(new User
            {
                Username = username, Password = PasswordHash.Create(password), IsPasswordHashed = true, Role = role
            });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 128)
                throw new ArgumentException("Use a password between 8 and 128 characters.", nameof(newPassword));

            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var user = await context.Users.FindAsync(userId);
            if (user == null || !user.IsPasswordHashed ||
                !PasswordHash.Verify(currentPassword ?? string.Empty, user.Password)) return false;

            user.Password = PasswordHash.Create(newPassword);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logService.LogInfo($"Password changed for user #{userId}.");
            return true;
        }

        public async Task<User> ValidateLoginAsync(string username, string password)
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            var normalizedPassword = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(normalizedPassword))
            {
                return null;
            }

            await _loginGate.WaitAsync();
            try
            {
                if (_clock.GetUtcNow() < _lockedUntil) return null;

                using (var context = _contexts.CreateDbContext())
                {
                    var settings = await context.Settings
                        .AsNoTracking()
                        .OrderBy(setting => setting.Id)
                        .FirstOrDefaultAsync();

                    if (settings == null)
                    {
                        throw new InvalidOperationException("ServeX setup is incomplete.");
                    }

                    var user = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(user => EF.Functions.Collate(user.Username, "NOCASE") == normalizedUsername);

                    if (user != null && (!user.IsPasswordHashed || !PasswordHash.Verify(normalizedPassword, user.Password)))
                        user = null;

                    if (user == null)
                    {
                        if (++_failedLogins >= 5)
                        {
                            _lockedUntil = _clock.GetUtcNow().AddSeconds(15);
                            _failedLogins = 0;
                        }
                        _logService.LogInfo(string.Format("Login failed for user {0}", normalizedUsername));
                    }
                    else
                    {
                        _failedLogins = 0;
                        _logService.LogInfo(string.Format("Login succeeded for user {0}", normalizedUsername));
                    }

                    return user;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Login validation failed for user {0}", normalizedUsername), exception);
                throw;
            }
            finally
            {
                _loginGate.Release();
            }
        }
    }
}
