﻿using EBankApp.Attributes;
using EBankApp.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Dynamic;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace EBankApp.Controllers
{
    public class UserController : BaseController
    {

        public UserController() : base()
        {
        }

        [HttpGet]
        [NoCache]
        public async Task<ActionResult> Login()
        {
            if (IsAlreadyLoggedIn())
            {
                await LogActivity(UserActivityEnum.USER_LOGIN);
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpGet]
        [NoCache]
        public async Task<ActionResult> Logout()
        {
            await LogActivity(UserActivityEnum.USER_LOGOUT);
            if (IsAlreadyLoggedIn())
            {
                HttpContext.Session.Remove("User");
                Session.Abandon();
                return RedirectToActionPermanent("Login");
            }

            return View("Error");
        }

        [HttpGet]
        [NoCache]
        public async Task<ActionResult> Register()
        {
            if (IsAlreadyLoggedIn())
            {
                await LogActivity(UserActivityEnum.USER_REGISTER);
                return RedirectToActionPermanent("Dashboard");
            }
            return View();
        }

        // GET: Account
        [HttpPost]
        public async Task<ActionResult> Login(LoginRequest user)
        {
            using (appDbContext)
            {
                var res = await appDbContext.Users.FirstOrDefaultAsync(x => (x.UserName == user.UserName && x.Password == user.Password) || x.Accounts.Any(a => a.AccountNumber == user.UserName && x.PIN == user.Password));
                if (res != null)
                {
                    var accounts = await appDbContext.Accounts.Where(x => x.UserId == res.Id).ToListAsync();
                    AttachToContext<User>("User", res);
                    AttachToContext<List<Account>>("Accounts", accounts);
                    await LogActivity(UserActivityEnum.USER_LOGIN);
                    return RedirectToActionPermanent("Dashboard");
                }
            }
            return View(user);
        }

        [HttpPost]
        public async Task<ActionResult> Register(User model)
        {
            int dbSaveResult = 0;
            int NumberOfTransactions = 2;
            if (ModelState.IsValid)
            {
                try
                {
                    var user = new User
                    {
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                        PIN = model.PIN,
                        UserName = model.UserName,
                        Password = model.Password,
                        RoleId = (int)UserRoleEnum.User
                    };

                    appDbContext.Users.Add(user);

                    var account = new Account()
                    {
                        AccountBalance = INITIAL_ACCOUNT_BALANCCE,
                        AccountNumber = EBankHelper.GenerateAccountNumber(ACCOUNT_NUMBER_LENGTH),
                        AccountType = AccountTypeEnum.SAVINGS,
                        UserId = user.Id,
                        Currency = (int)CurrencyCode.GBP
                    };

                    appDbContext.Accounts.Add(account);
                    dbSaveResult = await appDbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {

                    throw;
                }

                if (dbSaveResult == NumberOfTransactions)
                {
                    AttachToContext<User>("User", model);
                    await LogActivity(UserActivityEnum.USER_REGISTER);
                    return RedirectToActionPermanent("Dashboard");
                }
            }

            return View(model);
        }

        [HttpPost]
        public async Task<JsonResult> UsernameExists(string username)
        {
            bool available = true;
            if (!string.IsNullOrEmpty(username))
            {
                using (appDbContext)
                {
                    available = await appDbContext.Users.AnyAsync(x => x.UserName == username) ? false : true;
                }

                await Task.Delay(5000);
            }
            return Json(available);
        }

        [EBankAuthorized]
        public async Task<ActionResult> Dashboard()
        {
            // update session.
            await UpdateSession();
            return View();
        }

        [HttpGet]
        public async Task<ActionResult> MyProfile(string userId)
        {
            await LogActivity(UserActivityEnum.MY_PROFILE);
            using (appDbContext)
            {
                var user = await appDbContext.Users.FindAsync(Convert.ToInt32(userId));

                var accounts = await appDbContext.Accounts.Where(x => x.UserId == user.Id).ToListAsync();

                // update session.
                await UpdateSession(userId);

                return View(user);
            }
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> MyProfile(string userId, User userModel)
        {
            await LogActivity(UserActivityEnum.MY_PROFILE);
            using (appDbContext)
            {
                var user = await appDbContext.Users.FindAsync(Convert.ToInt32(userId));

                user.FirstName = userModel.FirstName;
                user.LastName = userModel.LastName;
                user.Password = userModel.Password;
                user.PIN = userModel.PIN;
                user.UserName = userModel.UserName;
                user.RoleId = userModel.RoleId;

                await appDbContext.SaveChangesAsync();

                var accounts = await appDbContext.Accounts.Where(x => x.UserId == user.Id).ToListAsync();

                // update session.
                await UpdateSession(userId);

                return RedirectToAction("MyProfile", "User", new { userId = user.Id });
            }
            return View();
        }

        [HttpGet]
        [EBankAuthorized]
        public async Task<ActionResult> GetUsers()
        {
            await LogActivity(UserActivityEnum.MY_ACCOUNTS);

            int start = Convert.ToInt32(HttpContext.Request["start"] ?? "1");
            int length = Convert.ToInt32(HttpContext.Request["length"] ?? "10");
            string searchValue = HttpContext.Request["search[value]"];
            string sortColumnName = HttpContext.Request["columns[" + Request["order[0][column]"] + "][name]"] ?? "FirstName";
            string sortDirection = HttpContext.Request["order[0][dir]"] ?? "asc";

            List<User> users;

            using (appDbContext)
            {
                users = await appDbContext.Users.AsNoTracking().ToListAsync();
                int totalrows = users.ToList().Count;
                if (!string.IsNullOrEmpty(searchValue))//filter
                {
                    users = users.Where(x => x.FirstName.ToLower().Contains(searchValue.ToLower())).ToList();
                }
                int totalrowsafterfiltering = users.ToList().Count;

                try
                {
                    if (!string.IsNullOrEmpty(sortColumnName) && !string.IsNullOrEmpty(sortDirection))
                        users = users.OrderBy<User>(sortColumnName + " " + sortDirection).ToList();
                }
                catch (Exception ex)
                {
                    throw;
                }

                //paging
                users = users.Skip(start).Take(length).ToList<User>();

                return Json(new { data = users, draw = Request["draw"], recordsTotal = totalrows, recordsFiltered = totalrowsafterfiltering }, JsonRequestBehavior.AllowGet);

            }
        }

        [HttpGet]
        [EBankAuthorized]
        public async Task<ActionResult> MyAccounts(string userId)
        {
            await LogActivity(UserActivityEnum.MY_ACCOUNTS);
            using (appDbContext)
            {
                var id = Convert.ToInt32(userId);
                var users = await appDbContext.Accounts.Where(x => x.UserId == id).ToListAsync();
                return View(users);
            }
            return View();
        }

        [HttpGet]
        [EBankAuthorized]
        public async Task<ActionResult> ManageUsers()
        {
            await LogActivity(UserActivityEnum.MANAGE_USERS);
            return View();
        }

        #region JS Methods

        [HttpPost]
        public async Task<ActionResult> DeleteUser(int userId)
        {
            using (appDbContext)
            {
                var user = await appDbContext.Users.Where(x => x.Id == userId).FirstOrDefaultAsync();
                appDbContext.Users.Remove(user);
                var result = await appDbContext.SaveChangesAsync();
                if (result > 0)
                {
                    return Json(new { Status = "success" });
                }
            }
            throw new Exception();
        }

        #endregion

        #region Private Methods
        private bool IsAlreadyLoggedIn()
        {
            var user = HttpContext.Session["User"] as User;
            if (user != null)
            {
                return true;
            }
            return false;
        }
        #endregion
    }
}