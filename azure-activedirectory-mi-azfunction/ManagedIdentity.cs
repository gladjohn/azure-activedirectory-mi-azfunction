using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;
using Azure.Security.KeyVault.Secrets;
using static System.Formats.Asn1.AsnWriter;
using System.Net.Http;
using System.Web;
using System.Net.Http.Headers;

namespace azure_activedirectory_mi_azfunction
{
    public class ManagedIdentity
    {
        private readonly HttpClient _httpclient;
        private readonly ILogger<ManagedIdentity> _log;

        public ManagedIdentity(IHttpClientFactory httpClientFactory, ILogger<ManagedIdentity> log)
        {
            _httpclient = httpClientFactory.CreateClient();
            _log = log;
        }

        [FunctionName("ManagedIdentity")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            _log.LogInformation("C# HTTP trigger function processed a request.");

            string client_id = req.Query["client_id"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            client_id ??= data?.client_id;

            string token = await GetManagedIdentityTokenUsingMSAL(_log, client_id).ConfigureAwait(false);

            var secret = await GetKeyVaultSecret(_log, token).ConfigureAwait(false);

            return new OkObjectResult("Secret Value is : " + secret);
        }

        private static async Task<string> GetManagedIdentityTokenUsingMSAL(
            ILogger log,
            string client_id = null)
        {
            var mia = CreateMia(client_id);
            string resource = "https://keyvaullt.azure.net";

            try
            {
                AuthenticationResult result = await mia.AcquireTokenForManagedIdentity(resource) 
                    .ExecuteAsync()
                    .ConfigureAwait(false);
                
                log.LogInformation("Access token acquired successfully.");

                return result.AccessToken;
            }
            catch (Exception ex) 
            {
                log.LogInformation("Unable to get an Access token.");

                throw new Exception("Exception occured while trying to acquire a token : " + ex.Message);
            }
        }

        private async Task<string> GetKeyVaultSecret(
            ILogger log,
            string token)
        {
            // Key-Vault Secret Identifier with api-version
            const string reqUrl = "https://msidlabs.vault.azure.net/secrets/msidlab1?api-version=7.0";
            string result;

            try
            {
                //Set the Authorization header
                if (_httpclient != null)
                {
                    _httpclient.DefaultRequestHeaders.Authorization
                        = new AuthenticationHeaderValue("Bearer", token);
                }

                var resp = await _httpclient.GetAsync(reqUrl).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    result = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                else 
                {
                    result = "Failed to get secret from the vault.";
                }

                return result;
            }
            catch (Exception ex)
            {
                log.LogInformation("Unable to get secret from the vault.");

                throw new Exception("Exception occured while trying to get the secret : " + ex.Message);
            }
        }

        private static IManagedIdentityApplication CreateMia(string client_id = null)
        {
            IManagedIdentityApplication mia;

            if (string.IsNullOrEmpty(client_id))
            {
                mia = ManagedIdentityApplicationBuilder.Create().WithExperimentalFeatures().Build();
            }
            else
            {
                mia = ManagedIdentityApplicationBuilder.Create(client_id).WithExperimentalFeatures().Build();
            }

            return mia;
        }
    }
}
