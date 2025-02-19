using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReadyPlayerMe.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace ReadyPlayerMe.AvatarCreator
{
    public class PartnerAssetsRequests
    {
        private const string TAG = nameof(PartnerAssetsRequests);
        private const int LIMIT = 100;

        private readonly AuthorizedRequest authorizedRequest;
        private readonly string appId;
        private readonly Dictionary<string, Texture> icons;

        public PartnerAssetsRequests(string appId)
        {
            authorizedRequest = new AuthorizedRequest();
            icons = new Dictionary<string, Texture>();
            this.appId = appId;
        }

        public async Task<PartnerAsset[]> Get(BodyType bodyType, OutfitGender gender, CancellationToken ctx = new CancellationToken())
        {
            var assets = new HashSet<PartnerAsset>();
            AssetData assetData;

            try
            {
                assetData = await GetRequest(LIMIT, 1, null, gender, bodyType, ctx: ctx);
                assets.UnionWith(assetData.Assets);
            }
            catch (Exception e)
            {
                return assets.ToArray();
            }

            var assetRequests = new Task<AssetData>[assetData.Pagination.TotalPages - 1];

            for (var i = 2; i <= assetData.Pagination.TotalPages; i++)
            {
                assetRequests[i - 2] = GetRequest(LIMIT, i, null, gender, bodyType, ctx: ctx);
            }

            while (!assetRequests.All(x => x.IsCompleted) && !ctx.IsCancellationRequested)
            {
                await Task.Yield();
            }

            foreach (var request in assetRequests.Where(request => request.IsCompleted))
            {
                try
                {
                    assets.UnionWith(request.Result.Assets);
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return assets.ToArray();
        }

        public async Task<PartnerAsset[]> Get(Category? category, BodyType bodyType, OutfitGender gender, CancellationToken ctx = new CancellationToken())
        {
            var assets = new HashSet<PartnerAsset>();
            var assetData = await GetRequest(LIMIT, 1, category, gender, bodyType, ctx: ctx);
            assets.UnionWith(assetData.Assets);

            for (var i = 2; i <= assetData.Pagination.TotalPages; i++)
            {
                assetData = await GetRequest(LIMIT, i, category, gender, bodyType, ctx: ctx);
                assets.UnionWith(assetData.Assets);
            }

            return assets.ToArray();
        }

        private async Task<AssetData> GetRequest(int limit, int pageNumber, Category? category, OutfitGender gender, BodyType bodyType, CancellationToken ctx = new CancellationToken())
        {
            var startTime = Time.time;

            var type = string.Empty;
            if (category != null)
            {
                type = CategoryHelper.PartnerCategoryMap.First(x => x.Value == category).Key;
            }
            
            var url = AssetEndpoints.GetAssetEndpoint(type, limit, pageNumber, AuthManager.UserSession.Id, appId, gender == OutfitGender.Masculine ? "male" : "female");
       
            var response = await authorizedRequest.SendRequest<Response>(new RequestData
            {
                Url = url,
                Method = HttpMethod.GET
            }, ctx);
            response.ThrowIfError();

            var json = JObject.Parse(response.Text);
            var partnerAssets = JsonConvert.DeserializeObject<PartnerAsset[]>(json["data"]!.ToString());
            var pagination = JsonConvert.DeserializeObject<Pagination>(json["pagination"]!.ToString());

            if (category != null)
            {
                SDKLogger.Log(TAG, $"Asset by category {category} with page {pageNumber} received: {Time.time - startTime}s");
            }
            else
            {
                SDKLogger.Log(TAG, $"Asset with page {pageNumber} received: {Time.time - startTime}s");
            }

            return new AssetData
            {
                Assets = partnerAssets,
                Pagination = pagination
            };
        }

        public async Task<Texture> GetAssetIcon(string url, Action<Texture> completed, CancellationToken ctx = new CancellationToken())
        {
            if (icons.ContainsKey(url))
            {
                completed?.Invoke(icons[url]);
                return icons[url];
            }

            var downloadHandler = new DownloadHandlerTexture();
            var response = await authorizedRequest.SendRequest<ResponseTexture>(new RequestData
            {
                Url = url,
                Method = HttpMethod.GET,
                DownloadHandler = downloadHandler
            }, ctx: ctx);

            response.ThrowIfError();

            // This check is needed because the same url can be requested multiple times
            if (!icons.ContainsKey(url))
            {
                icons.Add(url, response.Texture);
            }

            completed?.Invoke(response.Texture);
            return response.Texture;
        }
    }
}
