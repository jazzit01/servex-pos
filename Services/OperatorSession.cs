using System;
using ServeX.Models;

namespace ServeX.Services
{
    public sealed class OperatorSession
    {
        public User ActiveUser { get; set; }
        public bool MayManage => ActiveUser?.Role != UserRole.Cashier;

        public void RequireManager()
        {
            if (!MayManage) throw new InvalidOperationException("An administrator is required for this action.");
        }

        public bool CanManageStaff => ActiveUser?.Role == UserRole.Admin;
    }
}
