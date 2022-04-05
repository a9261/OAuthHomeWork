using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OAuthHomeWork.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace OAuthHomeWork.Controllers
{
    public class HomeController : Controller
    {
        public HttpClient HttpClient { get; }
        private readonly ILogger<HomeController> _logger;

        private readonly string _loginChannelId = "1657031544";
        private readonly string _loginChannelSecretId = "e52801e0c207c18d84afd835e33dc516";

        private readonly string _notifyClientId = "mzEPwTz9d7bdHCqcw3I0yi";
        private readonly string _notifySecretId = "N5zKXxH6wE3DSHtgLGIgKkdz2gqP9KBYGBIXUaAUfQq";

        private readonly string _loginSuccessRedirectUrl = "https://lineoauthhomework.azurewebsites.net/loginSuccessCallback";
        private readonly string _subscribeSuccessRedirectUrl = "https://lineoauthhomework.azurewebsites.net/subscribeSuccessCallback";

        private readonly string _envPath;

        public HomeController(ILogger<HomeController> logger, IHttpClientFactory httpClientFactory)
        {
            HttpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _envPath = Environment.CurrentDirectory + "\\userData.json";
        }

        public async Task<IActionResult> Index()
        {
            var (userProfile, user) = await ESureUserLogin();
            ViewBag.IsLoginSuccess = userProfile != null;
            if (userProfile != null)
            {
                ViewBag.UserName = userProfile.displayName;
                ViewBag.IsSubscribeSuccess = !string.IsNullOrEmpty(user.NotifyToken);
            }
            return View();
        }

        [HttpPost]
        [Route("lineLogin")]
        public async Task<IActionResult> LineLogin()
        {
            if (System.IO.File.Exists(_envPath))
            {
                var userId = Request.Cookies["UserId"];
                var userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
                var user = userData.FirstOrDefault(x => x.UserId == userId);
                if (user == null)
                {
                    return LineLoginRedirect();
                }
                var userProfile = await GetLineUserProfile(user.LoginToken);
                if (userProfile == null)
                {
                    return LineLoginRedirect();
                }
                ViewBag.IsLoginSuccess = true;
                ViewBag.IsSubscribeSuccess = false;
                ViewBag.UserName = userProfile.displayName;
                return View("Index");
            }
            else
            {
                return LineLoginRedirect();
            }
        }

        [HttpGet]
        [Route("loginSuccessCallback")]
        public async Task<IActionResult> LoginSuccessCallback()
        {
            var lineAuthCode = HttpContext.Request.Query["code"].ToString();
            ViewBag.IsSubscribeSuccess = false;

            var accessTokenParameter = new QueryBuilder();
            accessTokenParameter.Add("grant_type", "authorization_code");
            accessTokenParameter.Add("code", lineAuthCode);
            accessTokenParameter.Add("redirect_uri", _loginSuccessRedirectUrl);
            accessTokenParameter.Add("client_id", _loginChannelId);
            accessTokenParameter.Add("client_secret", _loginChannelSecretId);

            var value = accessTokenParameter.ToQueryString().Value;
            var issueAccessTokenContent = new StringContent($"{value.Replace("?", "")}", Encoding.UTF8, "application/x-www-form-urlencoded");
            var accessTokenResponse = await this.HttpClient.PostAsync($"https://api.line.me/oauth2/v2.1/token", issueAccessTokenContent);

            if (accessTokenResponse.StatusCode == HttpStatusCode.OK)
            {
                List<UserDataModel> userData = new List<UserDataModel>();

                var responseLoginToken = JsonConvert.DeserializeObject<LineTokenModel>((await accessTokenResponse.Content.ReadAsStringAsync()));

                //User Profile
                var userProfile = await GetLineUserProfile(responseLoginToken.access_token);
                var user = new UserDataModel()
                {
                    LoginToken = responseLoginToken.access_token,
                    UserId = responseLoginToken.id_token
                };

                if (System.IO.File.Exists(_envPath))
                {
                    userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
                    if (userData.Exists(x => x.UserId == user.UserId))
                    {
                        userData.Remove(user);
                    }
                }
                userData.Add(user);
                System.IO.File.WriteAllText(_envPath, JsonConvert.SerializeObject(userData));
                ViewBag.IsLoginSuccess = true;

                ViewBag.UserName = userProfile.displayName;
                Response.Cookies.Append("UserId", user.UserId);
            }
            else
            {
                ViewBag.IsLoginSuccess = false;
            }
            return View("Index");
        }

        [HttpPost]
        [Route("lineNotify")]
        public async Task<IActionResult> LineNotify()
        {
            var userId = Request.Cookies["UserId"];
            var userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
            var user = userData.FirstOrDefault(x => x.UserId == userId);

            var notifySuccess = await LineNotifySuccess(user.NotifyToken);
            if (notifySuccess)
            {
                return View("SubscribeSuccessCallback");
            }

            var subscribeParameter = new QueryBuilder();
            subscribeParameter.Add("response_type", "code");
            subscribeParameter.Add("client_id", _notifyClientId);
            subscribeParameter.Add("client_secret", _notifySecretId);
            subscribeParameter.Add("redirect_uri", _subscribeSuccessRedirectUrl);
            subscribeParameter.Add("scope", "notify");
            subscribeParameter.Add("state", userId);

            return Redirect($"https://notify-bot.line.me/oauth/authorize{subscribeParameter.ToQueryString().Value}");
        }

        [HttpGet]
        [Route("subscribeSuccessCallback")]
        public async Task<IActionResult> SubscribeSuccessCallback()
        {
            var lineAuthCode = HttpContext.Request.Query["code"].ToString();
            var userId = HttpContext.Request.Query["state"].ToString();

            var accessTokenParameter = new QueryBuilder();
            accessTokenParameter.Add("grant_type", "authorization_code");
            accessTokenParameter.Add("code", lineAuthCode);
            accessTokenParameter.Add("redirect_uri", _subscribeSuccessRedirectUrl);
            accessTokenParameter.Add("client_id", _notifyClientId);
            accessTokenParameter.Add("client_secret", _notifySecretId);

            var issueAccessTokenContent = new StringContent($"{accessTokenParameter.ToQueryString().Value.Replace("?", "")}", Encoding.UTF8, "application/x-www-form-urlencoded");
            var accessTokenResponse = await this.HttpClient.PostAsync($"https://notify-bot.line.me/oauth/token", issueAccessTokenContent);
            var responseLoginToken = JsonConvert.DeserializeObject<LineTokenModel>((await accessTokenResponse.Content.ReadAsStringAsync()));

            var userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
            var user = userData.FirstOrDefault(x => x.UserId == userId);
            user.NotifyToken = responseLoginToken.access_token;
            userData.Remove(user);
            userData.Add(user);
            System.IO.File.WriteAllText(_envPath, JsonConvert.SerializeObject(userData));

            await LineNotifySuccess(responseLoginToken.access_token);

            return View("SubscribeSuccessCallback");
        }

        [HttpPost]
        [Route("RevokeAll")]
        public async Task<IActionResult> RevokeAll()
        {
            var userId = Request.Cookies["UserId"];
            var userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
            var user = userData.FirstOrDefault(x => x.UserId == userId);

            //Revoke Login
            if (!string.IsNullOrEmpty(user.LoginToken))
            {
                await this.HttpClient.PostAsync($"https://api.line.me/oauth2/v2.1/revoke", new StringContent($"access_token={user.LoginToken}&client_id={_loginChannelId}&client_secret={_loginChannelSecretId}", Encoding.UTF8, "application/x-www-form-urlencoded"));
            }
            //Revoke Notify
            if (!string.IsNullOrEmpty(user.NotifyToken))
            {
                this.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.NotifyToken);
                await this.HttpClient.PostAsync($"https://notify-api.line.me/api/revoke", new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"));
            }

            userData.Remove(user);
            Response.Cookies.Delete("UserId");
            System.IO.File.WriteAllText(_envPath, JsonConvert.SerializeObject(userData));
            return View("RevokeAll");
        }

        private async Task<bool> LineNotifySuccess(string accessToken)
        {
            var notifyParameter = new QueryBuilder();
            notifyParameter.Add("message", "Subscribe Success");
            this.HttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            var content = new StringContent($"{notifyParameter.ToQueryString().Value.Replace("?", "")}", Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await this.HttpClient.PostAsync($"https://notify-api.line.me/api/notify", content);
            return response.StatusCode == HttpStatusCode.OK;
        }

        private async Task<(LineUserProfileModel, UserDataModel)> ESureUserLogin()
        {
            if (System.IO.File.Exists(_envPath))
            {
                var userId = Request.Cookies["UserId"];
                var userData = JsonConvert.DeserializeObject<List<UserDataModel>>(System.IO.File.ReadAllText(_envPath));
                var user = userData.FirstOrDefault(x => x.UserId == userId);
                if (user == null)
                {
                    return (null, null);
                }
                var userProfile = await GetLineUserProfile(user.LoginToken);
                if (userProfile == null)
                {
                    return (null, null);
                }
                return (userProfile, user);
            }

            return (null, null);
        }

        private IActionResult LineLoginRedirect()
        {
            var querystring = new QueryBuilder();
            querystring.Add("response_type", "code");
            querystring.Add("client_id", _loginChannelId);
            querystring.Add("redirect_uri", _loginSuccessRedirectUrl);
            querystring.Add("state", "abcddogeatpig");
            querystring.Add("scope", "profile openid mail");
            return Redirect($"https://access.line.me/oauth2/v2.1/authorize{querystring.ToQueryString().Value}");
        }

        private async Task<LineUserProfileModel> GetLineUserProfile(string accessToken)
        {
            this.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await this.HttpClient.GetAsync($"https://api.line.me/v2/profile");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }
            var userProfile = JsonConvert.DeserializeObject<LineUserProfileModel>((await response.Content.ReadAsStringAsync()));
            return userProfile;
        }
    }
}