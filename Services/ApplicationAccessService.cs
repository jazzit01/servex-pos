using System;
using System.Linq;
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

        public ApplicationAccessService(IDbContextFactory<ServeXDbContext> contexts, LogService logService)
        {
            _contexts = contexts;
            _logService = logService;
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

                    if (await context.Users.AnyAsync(user => user.Username.ToLower() == normalizedUsername.ToLower()))
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

        public async Task<User> ValidateLoginAsync(string username, string password)
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            var normalizedPassword = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(normalizedPassword))
            {
                return null;
            }

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
                        throw new InvalidOperationException("ServeX setup is incomplete.");
                    }

                    var user = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(user => user.Username.ToLower() == normalizedUsername.ToLower());

                    if (user != null && (!user.IsPasswordHashed || !PasswordHash.Verify(normalizedPassword, user.Password)))
                        user = null;

                    if (user == null)
                    {
                        _logService.LogInfo(string.Format("Login failed for user {0}", normalizedUsername));
                    }
                    else
                    {
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
        }
    }
}
