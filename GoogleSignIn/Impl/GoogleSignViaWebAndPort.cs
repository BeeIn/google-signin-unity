using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using System.Net;
using System.Net.NetworkInformation;

using UnityEngine;

using Newtonsoft.Json.Linq;
using System.Net.Sockets;

namespace Google.Impl
{
    internal class GoogleSignViaWebAndPort : ISignInImpl, FutureAPIImpl<GoogleSignInUser>
    {
        GoogleSignInConfiguration configuration;

        public bool Pending { get; private set; }

        public GoogleSignInStatusCode Status { get; private set; }

        public GoogleSignInUser Result { get; private set; }

        private Action<string> _openWebsiteImplem;

        public GoogleSignViaWebAndPort(GoogleSignInConfiguration configuration, Action<string> openWebsiteImplem)
        {
            this.configuration = configuration;
            _openWebsiteImplem = openWebsiteImplem;
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void EnableDebugLogging(bool flag)
        {
            throw new NotImplementedException();
        }

        public Future<GoogleSignInUser> SignIn()
        {
            SigningIn();
            return new Future<GoogleSignInUser>(this);
        }

        public Future<GoogleSignInUser> SignInSilently()
        {
            SigningIn();
            return new Future<GoogleSignInUser>(this);
        }

        public void SignOut()
        {
            Debug.Log("No need on editor?");
        }

        static TcpListener BindLocalHostFirstAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Any, 50001);
            listener.Start();

            return listener;
        }


        void SigningIn()
        {
            Pending = true;
            int port = 50001;
            var tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();

            try
            {
                string redirectUri = $"http://localhost:{port}/";
                var openURL = "https://accounts.google.com/o/oauth2/v2/auth?" +
                              Uri.EscapeUriString("scope=openid email profile&response_type=code&redirect_uri=" +
                                                  redirectUri +
                                                  "&client_id=" + configuration.WebClientId +
                                                  "&prompt=select_account");
                Debug.Log(openURL);
                _openWebsiteImplem(openURL);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }

            //var taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Run(async () =>
            {
                try
                {
                    using var client = await tcpListener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();

                    // Read raw request
                    using var reader = new StreamReader(stream, Encoding.ASCII);
                    string requestLine = await reader.ReadLineAsync(); // e.g., "GET /?code=xyz HTTP/1.1"
                    string line;
                    while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync())) { } // Read and discard headers

                    string query = requestLine?.Split(' ')[1]; // Extract "/?code=..."
                    var uri = new Uri("http://localhost" + query);
                    var queryDictionary = System.Web.HttpUtility.ParseQueryString(uri.Query);

                    if (queryDictionary == null || queryDictionary.Get("code") is not string code || string.IsNullOrEmpty(code))
                    {
                        string response = "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain\r\n\r\nCannot get code";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                        return;
                    }

                    // Send OK response
                    string okResponse = "HTTP/1.1 302 Found\r\n" +
                                        "Location: easycargo://open\r\n" +
                                        "Content-Length: 0\r\n" +
                                        "\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(okResponse));

                    // Exchange code for tokens
                    var tokenRequest = HttpWebRequest.CreateHttp("https://www.googleapis.com/oauth2/v4/token");
                    tokenRequest.Method = "POST";
                    tokenRequest.ContentType = "application/x-www-form-urlencoded";

                    string postData = $"code={code}&client_id={configuration.WebClientId}&client_secret={configuration.ClientSecret}&redirect_uri=http://localhost:{port}/&grant_type=authorization_code";
                    using (var writer = new StreamWriter(await tokenRequest.GetRequestStreamAsync()))
                    {
                        writer.Write(postData);
                    }

                    string tokenResponseText;
                    using (var response = await tokenRequest.GetResponseAsync())
                    using (var readerToken = new StreamReader(response.GetResponseStream()))
                    {
                        tokenResponseText = await readerToken.ReadToEndAsync();
                    }

                    var jobj = JObject.Parse(tokenResponseText);
                    var accessToken = (string)jobj.GetValue("access_token");

                    var user = new GoogleSignInUser();
                    if (configuration.RequestAuthCode)
                        user.AuthCode = code;

                    if (configuration.RequestIdToken)
                        user.IdToken = (string)jobj.GetValue("id_token");

                    // Get user info
                    var userInfoRequest = HttpWebRequest.CreateHttp("https://openidconnect.googleapis.com/v1/userinfo");
                    userInfoRequest.Method = "GET";
                    userInfoRequest.Headers.Add("Authorization", "Bearer " + accessToken);

                    string userInfoText;
                    using (var response = await userInfoRequest.GetResponseAsync())
                    using (var readerUser = new StreamReader(response.GetResponseStream()))
                    {
                        userInfoText = await readerUser.ReadToEndAsync();
                    }

                    var userInfo = JObject.Parse(userInfoText);
                    user.UserId = (string)userInfo.GetValue("sub");
                    user.DisplayName = (string)userInfo.GetValue("name");

                    if (configuration.RequestEmail)
                        user.Email = (string)userInfo.GetValue("email");

                    if (configuration.RequestProfile)
                    {
                        user.GivenName = (string)userInfo.GetValue("given_name");
                        user.FamilyName = (string)userInfo.GetValue("family_name");
                        user.ImageUrl = Uri.TryCreate((string)userInfo.GetValue("picture"), UriKind.Absolute, out var url) ? url : null;
                    }

                    Result = user;
                    Status = GoogleSignInStatusCode.SUCCESS;
                }
                catch (Exception e)
                {
                    Status = GoogleSignInStatusCode.ERROR;
                    Debug.LogException(e);
                    throw;
                }
                finally
                {
                    Pending = false;
                    tcpListener.Stop();
                }
            });

        }
    }

    public static class EditorExt
    {
        public static Task<string> Post(this HttpWebRequest request, string contentType, string data, Encoding encoding = null)
        {
            if (encoding == null)
                encoding = Encoding.UTF8;

            request.Method = "POST";
            request.ContentType = contentType;
            using (var stream = request.GetRequestStream())
                stream.Write(encoding.GetBytes(data));

            return request.GetResponseAsStringAsync(encoding);
        }

        public static async Task<string> GetResponseAsStringAsync(this HttpWebRequest request, Encoding encoding = null)
        {
            using (var response = await request.GetResponseAsync())
            {
                using (var stream = response.GetResponseStream())
                    return stream.ReadToEnd(encoding ?? Encoding.UTF8);
            }
        }

        public static string ReadToEnd(this Stream stream, Encoding encoding = null) => new StreamReader(stream, encoding ?? Encoding.UTF8).ReadToEnd();
        public static void Write(this Stream stream, byte[] data) => stream.Write(data, 0, data.Length);
    }
}