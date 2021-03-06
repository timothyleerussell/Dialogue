﻿namespace Dialogue.Logic.Controllers.OAuthControllers
{
    using System;
    using System.Web.Mvc;
    using System.Web.Security;
    using Application;
    using Constants;
    using Models;
    using Models.ViewModels;
    using Skybrud.Social.Facebook;
    using Skybrud.Social.Facebook.Fields;
    using Skybrud.Social.Facebook.OAuth;
    using Skybrud.Social.Facebook.Options.User;

    // Facebook uses OAuth 2.0 for authentication and communication. In order for users to authenticate with the Facebook API, 
    // you must specify the ID, secret and redirect URI of your Facebook app. 
    // You can create a new app at the following URL: https://developers.facebook.com/

    public partial class FacebookOAuthController : DialogueBaseController
    {
        public string ReturnUrl => string.Concat(AppHelpers.ReturnCurrentDomain(), Urls.GenerateUrl(Urls.UrlType.FacebookLogin));
        public string Callback { get; private set; }
        public string ContentTypeAlias { get; private set; }
        public string PropertyAlias { get; private set; }

        /// <summary>
        /// Gets the authorizing code from the query string (if specified).
        /// </summary>
        public string AuthCode => Request.QueryString["code"];
        public string AuthState => Request.QueryString["state"];
        public string AuthErrorReason => Request.QueryString["error_reason"];
        public string AuthError => Request.QueryString["error"];
        public string AuthErrorDescription => Request.QueryString["error_description"];

        public ActionResult FacebookLogin()
        {
            var resultMessage = new GenericMessageViewModel();

            Callback = Request.QueryString["callback"];
            ContentTypeAlias = Request.QueryString["contentTypeAlias"];
            PropertyAlias = Request.QueryString["propertyAlias"];

            if (AuthState != null)
            {
                var stateValue = Session["Dialogue_" + AuthState] as string[];
                if (stateValue != null && stateValue.Length == 3)
                {
                    Callback = stateValue[0];
                    ContentTypeAlias = stateValue[1];
                    PropertyAlias = stateValue[2];
                }
            }

            // Get the prevalue options
            if (string.IsNullOrEmpty(Dialogue.Settings().FacebookAppId) || string.IsNullOrEmpty(Dialogue.Settings().FacebookAppSecret))
            {
                resultMessage.Message = "You need to add the Facebook app credentials";
                resultMessage.MessageType = GenericMessages.Danger;
            }
            else
            {

                // Settings valid move on
                // Configure the OAuth client based on the options of the prevalue options
                var client = new FacebookOAuthClient
                {
                    AppId = Dialogue.Settings().FacebookAppId,
                    AppSecret = Dialogue.Settings().FacebookAppSecret,
                    RedirectUri = ReturnUrl
                };

                // Session expired?
                if (AuthState != null && Session["Dialogue_" + AuthState] == null)
                {
                    resultMessage.Message = "Session Expired";
                    resultMessage.MessageType = GenericMessages.Danger;
                }

                // Check whether an error response was received from Facebook
                if (AuthError != null)
                {
                    resultMessage.Message = AuthErrorDescription;
                    resultMessage.MessageType = GenericMessages.Danger;
                }

                // Redirect the user to the Facebook login dialog
                if (AuthCode == null)
                {
                    // Generate a new unique/random state
                    var state = Guid.NewGuid().ToString();

                    // Save the state in the current user session
                    Session["Dialogue_" + state] = new[] { Callback, ContentTypeAlias, PropertyAlias };

                    // Construct the authorization URL
                    var url = client.GetAuthorizationUrl(state, "public_profile", "email"); //"user_friends"

                    // Redirect the user
                    return Redirect(url);
                }

                // Exchange the authorization code for a user access token
                var userAccessToken = string.Empty;
                try
                {
                    userAccessToken = client.GetAccessTokenFromAuthCode(AuthCode);
                }
                catch (Exception ex)
                {
                    resultMessage.Message = $"Unable to acquire access token<br/>{ex.Message}";
                    resultMessage.MessageType = GenericMessages.Danger;
                }

                try
                {
                    if (string.IsNullOrEmpty(resultMessage.Message))
                    {
                        // Initialize the Facebook service (no calls are made here)
                        var service = FacebookService.CreateFromAccessToken(userAccessToken);

                        // Declare the options for the call to the API
                        var options = new FacebookGetUserOptions
                        {
                            Identifier = "me",
                            Fields = new[] { "id", "name", "email", "first_name", "last_name", "gender" }
                        };

                        var user = service.Users.GetUser(options);

                        // Try to get the email - Some FB accounts have protected passwords
                        var email = user.Body.Email;
                        if (string.IsNullOrEmpty(email))
                        {
                            //maybe use 'user.Body.Id @ facebook.com'

                            resultMessage.Message = "Unable to get email address from Facebook";
                            resultMessage.MessageType = GenericMessages.Danger;
                            ShowMessage(resultMessage);
                            return RedirectToUmbracoPage(Dialogue.Settings().ForumId);
                        }

                        // First see if this user has registered already - Use email address
                        using (UnitOfWorkManager.NewUnitOfWork())
                        {

                            var userExists = AppHelpers.UmbServices().MemberService.GetByEmail(email);

                            if (userExists != null)
                            {
                                // Update access token
                                userExists.Properties[AppConstants.PropMemberFacebookAccessToken].Value = userAccessToken;
                                AppHelpers.UmbServices().MemberService.Save(userExists);

                                // Users already exists, so log them in
                                FormsAuthentication.SetAuthCookie(userExists.Username, true);
                                resultMessage.Message = Lang("Members.NowLoggedIn");
                                resultMessage.MessageType = GenericMessages.Success;
                            }
                            else
                            {
                                // Not registered already so register them
                                var viewModel = new RegisterViewModel
                                {
                                    Email = email,
                                    LoginType = LoginType.Facebook,
                                    Password = AppHelpers.RandomString(8),
                                    UserName = user.Body.Name,
                                    UserAccessToken = userAccessToken
                                };

                                // Get the image and save it
                                var getImageUrl = $"http://graph.facebook.com/{user.Body.Id}/picture?type=square";
                                viewModel.SocialProfileImageUrl = getImageUrl;

                                //Large size photo https://graph.facebook.com/{facebookId}/picture?type=large
                                //Medium size photo https://graph.facebook.com/{facebookId}/picture?type=normal
                                //Small size photo https://graph.facebook.com/{facebookId}/picture?type=small
                                //Square photo https://graph.facebook.com/{facebookId}/picture?type=square

                                return RedirectToAction("MemberRegisterLogic", "DialogueRegister", viewModel);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultMessage.Message = $"Unable to get user information<br/>{ex.Message}";
                    resultMessage.MessageType = GenericMessages.Danger;
                }

            }

            ShowMessage(resultMessage);
            return RedirectToUmbracoPage(Dialogue.Settings().ForumId);
        }
    }
}