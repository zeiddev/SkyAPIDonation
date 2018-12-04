using Newtonsoft.Json.Linq;
using Stripe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace SimpleDonation.Controllers
{
    public class DonationController : ApiController
    {
       
        public class DonationDTO
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string  Email { get; set; }
            public string Token { get; set; }
            public string Amount { get; set; }
            public string TextOther { get; set; }
        }

        [Route("donation")]
        [HttpPost]
        public async Task<IHttpActionResult> CaptureDonation([FromBody] DonationDTO formData)
        {

            var stripeResult = await SendToStripe(formData);
            var reResult = await SendToRaisersEdge(formData);
            

            return Ok(stripeResult && reResult);
        }

        private async Task<bool> SendToRaisersEdge(DonationDTO formData)
        {
            string giftId = string.Empty;
            string id = await LookupConstituentByEmail(formData);
            if (string.IsNullOrEmpty(id))
            {
                id = await CreateConstituent(formData);
            }

            if (!string.IsNullOrEmpty(id))
            {
                giftId = await CreateGift(formData, id);
            }

            return (!string.IsNullOrEmpty(giftId));
        }

        private async Task<string> LookupConstituentByEmail(DonationDTO formData)
        {

            string id = string.Empty;

            

            using (var http = new HttpClient())
            {
                //This is our call to the SKY API to look up the constituent
                var uri = "https://api.sky.blackbaud.com/constituent/v1/constituents/search?search_text=" + WebUtility.UrlEncode(formData.Email) + "&include_inactive=true&strict_search=false";
                var request = GetNXTRequest(uri, HttpMethod.Get);

                HttpResponseMessage response = await http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {

                    JObject jContent = JObject.Parse(await response.Content.ReadAsStringAsync());
                    int totalItems = Convert.ToInt32(jContent.SelectToken("count").ToString());

                    if (totalItems == 0)
                    {
                        return id;
                    }

                    JArray values = (JArray)jContent.SelectToken("value");

                    if (values != null)
                    {
                        //We are being lazy and just taking the first one that we find. You would want to ensure that you find the correct one.
                        id = values[0].SelectToken("id").Value<string>();
                       
                    }
                       
                }
            }

            return id;
        }

        private async Task<string> CreateConstituent(DonationDTO formData)
        {

            string id = string.Empty;

            using (var http = new HttpClient())
            {
                //This is our call to the SKY API to look up the constituent
                var uri = "https://api.sky.blackbaud.com/constituent/v1/constituents";
                var request = GetNXTRequest(uri, HttpMethod.Post);
                request.Content = new StringContent(ConstructConstituent(formData), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {

                    JObject jContent = JObject.Parse(await response.Content.ReadAsStringAsync());
                    
                        //We are being lazy and just assuming that the constituent was created successfully here...
                    id = jContent.SelectToken("id").Value<string>();
                    
                }
            }

            return id;

        }


        private string ConstructConstituent(DonationDTO formData)
        {

            JObject constituent = new JObject();
            constituent.Add("type", "Individual");
            constituent.Add("first", formData.FirstName);
            constituent.Add("last", formData.LastName);
            constituent.Add(new JProperty("email",
                new JObject(
                    new JProperty("address", formData.Email),
                    new JProperty("type", "Email"),
                    new JProperty("primary", true)
                    )
               )
            );

            return constituent.ToString();
        }



        private async Task<string> CreateGift(DonationDTO formData, string constitId)
        {

            string id = string.Empty;

            using (var http = new HttpClient())
            {
                //This is our call to the SKY API to look up the constituent
                var uri = "https://api.sky.blackbaud.com/gift/v1/gifts";
                var request = GetNXTRequest(uri, HttpMethod.Post);
                request.Content = new StringContent(ConstructGift(formData,constitId), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {

                    JObject jContent = JObject.Parse(await response.Content.ReadAsStringAsync());

                    //We are being lazy and just assuming that the constituent was created successfully here...
                    id = jContent.SelectToken("id").Value<string>();

                }
                else
                {
                    string message = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.Print(message);
                }
            }

            return id;

        }


        private string ConstructGift(DonationDTO formData, string id)
        {

            //The Stripe amount was in cents/pence. We need to convert that back to a value with decimals
            double amount = 0;
            if (formData.TextOther == null)
                amount = double.Parse(formData.Amount);
            else
                amount = double.Parse(formData.TextOther);

            JObject gift = new JObject();
            gift.Add("constituent_id", id);
            gift.Add("type", "Donation");
            gift.Add("date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));
            gift.Add("post_date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));
            gift.Add("post_status", "NotPosted");

            JProperty jAmount = new JProperty("amount",
                new JObject(
                    new JProperty("value", amount)
               )
            );

            gift.Add(jAmount);
            gift.Add(new JProperty("gift_splits", new JArray(
                new JObject(
                    jAmount,              
                    new JProperty("appeal_id", "25"),
                    new JProperty("campaign_id", "2"),
                    new JProperty("fund_id", "5")
                ))
                )
            );

            gift.Add(new JProperty("payments", new JArray(
                new JObject(new JProperty("payment_method", "Cash"))
               ))
            );

            return gift.ToString();
        }


        private HttpRequestMessage GetNXTRequest(string uri, HttpMethod method)
        {
            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri),
                Method = method,
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("Authorization", "Bearer " + GetAccessToken());
            request.Headers.Add("bb-api-subscription-key", "<Your BB API Subscription key>");

            return request;
        }

        private string GetAccessToken()
        {
            //You will need to figure out your own routine here. For testing generate an access token from the "Try it" app on the BB developer site.
            return "<Generated Token>";
        }

        private async Task<bool> SendToStripe(DonationDTO formData)
        {

            //This could really do with some better validation than just assuming the values to be numbers.
            int amount;
            if(formData.TextOther !=null)
            {
                amount = int.Parse(formData.TextOther) *100;
            }
            else
            {
                amount = int.Parse(formData.Amount) *100;
            }

            // See your keys here: https://dashboard.stripe.com/account/apikeys
            StripeConfiguration.SetApiKey("<Enter your key here>");

            var token = formData.Token; 

            try
            {
                var options = new ChargeCreateOptions
                {
                    Amount = amount,
                    Currency = "usd",
                    Description = "Generous Donation to Zeidman Development",
                    SourceId = token,
                };
                var service = new ChargeService();
                Charge charge = await service.CreateAsync(options);

            }
            catch (Exception ex)
            {
                //Again better exception handling would be required.
                return false;
            }

            return true;
        }
    }
}
