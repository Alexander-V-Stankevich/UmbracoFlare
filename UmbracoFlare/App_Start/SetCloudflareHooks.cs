﻿using UmbracoFlare.Configuration;
using UmbracoFlare.Manager;
using UmbracoFlare.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Publishing;
using Umbraco.Core.Services;
using Umbraco.Web.Models.Trees;
using Umbraco.Web.Trees;
using UmbracoFlare.ImageCropperHelpers;
using UmbracoFlare.Models.CropModels;
using Umbraco.Web;
using Umbraco.Core.Logging;
using UmbracoFlare.Helpers;
using Umbraco.Web.Cache;
using Umbraco.Core.Cache;



namespace UmbracoFlare.App_Start
{
    public class SetCloudflareHooks : ApplicationEventHandler
    {
        public SetCloudflareHooks()
            : base()
        {
            ContentService.Published += PurgeCloudflareCache;
            ContentService.Published += UpdateContentIdToUrlCache;

            PageCacheRefresher.CacheUpdated += UpdateContentIdToUrlCache;

            MediaService.Saved += PurgeCloudflareCacheForMedia;
            DataTypeService.Saved += RefreshImageCropsCache;
            TreeControllerBase.MenuRendering += AddPurgeCacheForContentMenu;
        }


        protected void UpdateContentIdToUrlCache(IPublishingStrategy strategy, PublishEventArgs<IContent> e)
        {

            UmbracoHelper uh = new UmbracoHelper(UmbracoContext.Current);
            
            foreach(IContent c in e.PublishedEntities)
            {
                if(c.HasPublishedVersion)
                {
                    string url = UmbracoContext.Current.UrlProvider.GetUrl(c.Id, Umbraco.Web.Routing.UrlProviderMode.AutoLegacy);

                    UmbracoUrlWildCardManager.Instance.UpdateContentIdToUrlCache(c.Id, url);
                }   
            }
        }

        protected override void ApplicationStarting(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            base.ApplicationStarting(umbracoApplication, applicationContext);
        }


        protected void RefreshImageCropsCache(IDataTypeService sender, SaveEventArgs<IDataTypeDefinition> e)
        {
            //A data type has saved, see if it was a 
            IEnumerable<IDataTypeDefinition> imageCroppers = ImageCropperManager.Instance.GetImageCropperDataTypes(true);
            IEnumerable<IDataTypeDefinition> freshlySavedImageCropper = imageCroppers.Intersect(e.SavedEntities);

            if(imageCroppers.Intersect(e.SavedEntities).Any())
            {
                //There were some freshly saved Image cropper data types so refresh the image crop cache.
                //We can do that by simply getting the crops
                ImageCropperManager.Instance.GetAllCrops(true); //true to bypass the cache & refresh it.
            }
        }


        protected void PurgeCloudflareCacheForMedia(IMediaService sender, SaveEventArgs<IMedia> e)
        {
            //If we have the cache buster turned off then just return.
            if (!CloudflareConfiguration.Instance.PurgeCacheOn) { return; }

            List<Crop> imageCropSizes = ImageCropperManager.Instance.GetAllCrops();
            List<string> urls = new List<string>();

            UmbracoHelper uh = new UmbracoHelper(UmbracoContext.Current);

           
            //delete the cloudflare cache for the saved entities.
            foreach (IMedia media in e.SavedEntities)
            {
                try
                {
                    //Check to see if the page has cache purging on publish disabled.
                    if (media.GetValue<bool>("cloudflareDisabledOnPublish"))
                    {
                        //it was disabled so just continue;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    //continue;
                } 

                IPublishedContent publishedMedia = uh.TypedMedia(media.Id);

                if (publishedMedia == null)
                {
                    e.Messages.Add(new EventMessage("Cloudflare Caching", "We could not find the IPublishedContent version of the media: "+media.Id+" you are trying to save.", EventMessageType.Error));
                    continue;
                }
                foreach(Crop crop in imageCropSizes)
                {
                    urls.Add(UrlHelper.MakeFullUrlWithDomain(publishedMedia.GetCropUrl(crop.alias)));    

                }
                urls.Add(UrlHelper.MakeFullUrlWithDomain(publishedMedia.Url));
            }

            List<StatusWithMessage> results = CloudflareManager.Instance.PurgePages(urls);

            if (results.Any() && results.Where(x => !x.Success).Any())
            {
                e.Messages.Add(new EventMessage("Cloudflare Caching", "We could not purge the Cloudflare cache. \n \n" + CloudflareManager.PrintResultsSummary(results), EventMessageType.Warning));
            }
            else if(results.Any())
            {
                e.Messages.Add(new EventMessage("Cloudflare Caching", "Successfully purged the cloudflare cache.", EventMessageType.Success));
            }
        }

        
        protected void UpdateContentIdToUrlCache(PageCacheRefresher refresher, CacheRefresherEventArgs args)
        {
           
            UmbracoHelper uh = new UmbracoHelper(UmbracoContext.Current);
            //UmbracoContext.Current.UrlProvider.GetUrl(case.Id)
            /*foreach(IContent c in e.PublishedEntities)
            {
                if(c.HasPublishedVersion)
                {
                    string url = UmbracoContext.Current.UrlProvider.GetUrl(c.Id);

                    UmbracoUrlWildCardManager.Instance.UpdateContentIdToUrlCache(c.Id, url);
                }   
            }*/
        }


        protected void PurgeCloudflareCache(IPublishingStrategy strategy, PublishEventArgs<IContent> e)
        {
            //If we have the cache buster turned off then just return.
            if (!CloudflareConfiguration.Instance.PurgeCacheOn) { return; }

            List<string> urls = new List<string>();
            //Else we can continue to delete the cache for the saved entities.
            foreach(IContent content in e.PublishedEntities)
            {
                try
                {
                    //Check to see if the page has cache purging on publish disabled.
                    if(content.GetValue<bool>("cloudflareDisabledOnPublish"))
                    {
                        //it was disabled so just continue;
                        continue;
                    }
                }
                catch(Exception ex)
                {
                    //continue;
                }
                
                urls.Add(umbraco.library.NiceUrlWithDomain(content.Id));
            }

            List<StatusWithMessage> results = CloudflareManager.Instance.PurgePages(urls);

            if (results.Any() && results.Where(x => !x.Success).Any())
            {
                e.Messages.Add(new EventMessage("Cloudflare Caching", "We could not purge the Cloudflare cache. \n \n" + CloudflareManager.PrintResultsSummary(results), EventMessageType.Warning));
            }
            else if (results.Any())
            {
                e.Messages.Add(new EventMessage("Cloudflare Caching", "Successfully purged the cloudflare cache.", EventMessageType.Success));
            }
        }

        private void AddPurgeCacheForContentMenu(TreeControllerBase sender, MenuRenderingEventArgs args)
        {
            if(sender.TreeAlias != "content")
            {
                //We aren't dealing with the content menu
                return;
            }

            MenuItem menuItem = new MenuItem("purgeCache", "Purge Cloudflare Cache");

            menuItem.Icon = "umbracoflare-tiny";

            menuItem.LaunchDialogView("/App_Plugins/UmbracoFlare/backoffice/treeViews/PurgeCacheDialog.html", "Purge Cloudflare Cache");

            args.Menu.Items.Insert(args.Menu.Items.Count - 1, menuItem);
        }

    }
}
