using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Models;
using ServeX.ViewModels;

namespace ServeX.Application
{
    // Runtime arguments (authenticated user, order ID and callbacks) are supplied here,
    // keeping container access confined to the composition boundary.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class PresentationFactory
    {
        private readonly IServiceProvider _services;
        public PresentationFactory(IServiceProvider services) { _services = services; }
        public MainViewModel CreateMain(AppSetting settings, User user) =>
            ActivatorUtilities.CreateInstance<MainViewModel>(_services, settings, user);
        public AuthWindowViewModel CreateAuth(Func<User, Task> authenticated) =>
            ActivatorUtilities.CreateInstance<AuthWindowViewModel>(_services, authenticated);
        public SetupViewModel CreateSetup(Func<string, Task> completed) =>
            ActivatorUtilities.CreateInstance<SetupViewModel>(_services, completed);
        public LoginViewModel CreateLogin(string restaurant, Func<User, Task> authenticated, string username, string status) =>
            ActivatorUtilities.CreateInstance<LoginViewModel>(_services, restaurant, authenticated, username, status);
    }
}
