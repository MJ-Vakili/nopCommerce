using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Xml;
using Nop.Core;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Forums;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Security;
using Nop.Services.Catalog;
using Nop.Services.Topics;

namespace Nop.Services.Seo
{
    /// <summary>
    /// Represents a sitemap generator
    /// </summary>
    public partial class SitemapGenerator : ISitemapGenerator
    {
        #region Constants

        private const string DateFormat = @"yyyy-MM-dd";

        /// <summary>
        /// At now each provided sitemap file must have no more than 50000 URLs
        /// </summary>
        private const int maxSitemapUrlNumber = 50000;

        #endregion

        #region Fields

        private readonly IStoreContext _storeContext;
        private readonly ICategoryService _categoryService;
        private readonly IProductService _productService;
        private readonly IManufacturerService _manufacturerService;
        private readonly ITopicService _topicService;
        private readonly IWebHelper _webHelper;
        private readonly CommonSettings _commonSettings;
        private readonly BlogSettings _blogSettings;
        private readonly NewsSettings _newsSettings;
        private readonly ForumSettings _forumSettings;
        private readonly SecuritySettings _securitySettings;

        private XmlTextWriter _writer;

        #endregion

        #region Ctor

        public SitemapGenerator(IStoreContext storeContext,
            ICategoryService categoryService,
            IProductService productService,
            IManufacturerService manufacturerService,
            ITopicService topicService,
            IWebHelper webHelper,
            CommonSettings commonSettings,
            BlogSettings blogSettings,
            NewsSettings newsSettings,
            ForumSettings forumSettings,
            SecuritySettings securitySettings)
        {
            this._storeContext = storeContext;
            this._categoryService = categoryService;
            this._productService = productService;
            this._manufacturerService = manufacturerService;
            this._topicService = topicService;
            this._webHelper = webHelper;
            this._commonSettings = commonSettings;
            this._blogSettings = blogSettings;
            this._newsSettings = newsSettings;
            this._forumSettings = forumSettings;
            this._securitySettings = securitySettings;
        }

        #endregion

        #region Nested class

        /// <summary>
        /// Represents sitemap URL entry
        /// </summary>
        protected class SitemapUrl
        {
            public SitemapUrl(string location, UpdateFrequency frequency, DateTime updatedOn)
            {
                Location = location;
                UpdateFrequency = frequency;
                UpdatedOn = updatedOn;
            }

            /// <summary>
            /// Gets or sets URL of the page
            /// </summary>
            public string Location { get; set; }

            /// <summary>
            /// Gets or sets a value indicating how frequently the page is likely to change
            /// </summary>
            public UpdateFrequency UpdateFrequency { get; set; }

            /// <summary>
            /// Gets or sets the date of last modification of the file
            /// </summary>
            public DateTime UpdatedOn { get; set; }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Get HTTP protocol
        /// </summary>
        /// <returns>Protocol name as string</returns>
        protected virtual string GetHttpProtocol()
        {
            return _securitySettings.ForceSslForAllPages ? "https" : "http";
        }

        /// <summary>
        /// Writes the url location to the writer.
        /// </summary>
        /// <param name="url">Url of indexed location (don't put root url information in).</param>
        /// <param name="updateFrequency">Update frequency - always, hourly, daily, weekly, yearly, never.</param>
        /// <param name="lastUpdated">Date last updated.</param>
        protected virtual void WriteUrlLocation(string url, UpdateFrequency updateFrequency, DateTime lastUpdated)
        {
            _writer.WriteStartElement("url");
            string loc = XmlHelper.XmlEncode(url);
            _writer.WriteElementString("loc", loc);
            _writer.WriteElementString("changefreq", updateFrequency.ToString().ToLowerInvariant());
            _writer.WriteElementString("lastmod", lastUpdated.ToString(DateFormat));
            _writer.WriteEndElement();
        }

        /// <summary>
        /// Generate URLs for the sitemap
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <returns>List of URL for the sitemap</returns>
        protected virtual IList<SitemapUrl> GenerateUrls(UrlHelper urlHelper)
        {
            var sitemapUrls = new List<SitemapUrl>();

            //home page
            var homePageUrl = urlHelper.RouteUrl("HomePage", null, GetHttpProtocol());
            sitemapUrls.Add(new SitemapUrl(homePageUrl, UpdateFrequency.Weekly, DateTime.UtcNow));

            //search products
            var productSearchUrl = urlHelper.RouteUrl("ProductSearch", null, GetHttpProtocol());
            sitemapUrls.Add(new SitemapUrl(productSearchUrl, UpdateFrequency.Weekly, DateTime.UtcNow));

            //contact us
            var contactUsUrl = urlHelper.RouteUrl("ContactUs", null, GetHttpProtocol());
            sitemapUrls.Add(new SitemapUrl(contactUsUrl, UpdateFrequency.Weekly, DateTime.UtcNow));

            //news
            if (_newsSettings.Enabled)
            {
                var url = urlHelper.RouteUrl("NewsArchive", null, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, DateTime.UtcNow));
            }

            //blog
            if (_blogSettings.Enabled)
            {
                var url = urlHelper.RouteUrl("Blog", null, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, DateTime.UtcNow));
            }

            //blog
            if (_forumSettings.ForumsEnabled)
            {
                var url = urlHelper.RouteUrl("Boards", null, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, DateTime.UtcNow));
            }

            //categories
            if (_commonSettings.SitemapIncludeCategories)
                WriteCategories(urlHelper, sitemapUrls, 0);

            //manufacturers
            if (_commonSettings.SitemapIncludeManufacturers)
                WriteManufacturers(urlHelper, sitemapUrls);

            //products
            if (_commonSettings.SitemapIncludeProducts)
                WriteProducts(urlHelper, sitemapUrls);

            //topics
            WriteTopics(urlHelper, sitemapUrls);

            //custom URLs
            WriteCustomUrls(sitemapUrls);

            return sitemapUrls;
        }

        /// <summary>
        /// Write category URLs to sitemap
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="sitemapUrls">Current list of URL</param>
        /// <param name="parentCategoryId">Parent category identifier</param>
        protected virtual void WriteCategories(UrlHelper urlHelper, List<SitemapUrl> sitemapUrls, int parentCategoryId)
        {
            var categories = _categoryService.GetAllCategoriesByParentCategoryId(parentCategoryId);
            foreach (var category in categories)
            {
                var url = urlHelper.RouteUrl("Category", new { SeName = category.GetSeName() }, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, category.UpdatedOnUtc));

                WriteCategories(urlHelper, sitemapUrls, category.Id);
            }
        }

        /// <summary>
        /// Write manufacturer URLs to sitemap
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="sitemapUrls">Current list of URL</param>
        protected virtual void WriteManufacturers(UrlHelper urlHelper, List<SitemapUrl> sitemapUrls)
        {
            var manufacturers = _manufacturerService.GetAllManufacturers(storeId: _storeContext.CurrentStore.Id);
            foreach (var manufacturer in manufacturers)
            {
                var url = urlHelper.RouteUrl("Manufacturer", new { SeName = manufacturer.GetSeName() }, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, manufacturer.UpdatedOnUtc));
            }
        }

        /// <summary>
        /// Write product URLs to sitemap
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="sitemapUrls">Current list of URL</param>
        protected virtual void WriteProducts(UrlHelper urlHelper, List<SitemapUrl> sitemapUrls)
        {
            var products = _productService.SearchProducts(
                storeId: _storeContext.CurrentStore.Id,
                visibleIndividuallyOnly: true,
                orderBy: ProductSortingEnum.CreatedOn);

            foreach (var product in products)
            {
                var url = urlHelper.RouteUrl("Product", new { SeName = product.GetSeName() }, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, product.UpdatedOnUtc));
            }
        }

        /// <summary>
        /// Write topic URLs to sitemap
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="sitemapUrls">Current list of URL</param>
        protected virtual void WriteTopics(UrlHelper urlHelper, List<SitemapUrl> sitemapUrls)
        {
            var topics = _topicService.GetAllTopics(_storeContext.CurrentStore.Id).Where(t => t.IncludeInSitemap).ToList();
            foreach (var topic in topics)
            {
                var url = urlHelper.RouteUrl("Topic", new { SeName = topic.GetSeName() }, GetHttpProtocol());
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, DateTime.UtcNow));
            }
        }

        /// <summary>
        /// Write custom URLs to sitemap
        /// </summary>
        /// <param name="sitemapUrls">Current list of URL</param>
        protected virtual void WriteCustomUrls(List<SitemapUrl> sitemapUrls)
        {
            foreach (var customUrl in _commonSettings.SitemapCustomUrls)
            {
                var url = _webHelper.GetStoreLocation() + customUrl;
                sitemapUrls.Add(new SitemapUrl(url, UpdateFrequency.Weekly, DateTime.UtcNow));
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// This will build an xml sitemap for better index with search engines.
        /// See http://en.wikipedia.org/wiki/Sitemaps for more information.
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="id">Sitemap identifier</param>
        /// <returns>Sitemap.xml as string</returns>
        public virtual string Generate(UrlHelper urlHelper, int? id)
        {
            using (var stream = new MemoryStream())
            {
                Generate(urlHelper, stream, id);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// This will build an xml sitemap for better index with search engines.
        /// See http://en.wikipedia.org/wiki/Sitemaps for more information.
        /// </summary>
        /// <param name="urlHelper">URL helper</param>
        /// <param name="id">Sitemap identifier</param>
        /// <param name="stream">Stream of sitemap.</param>
        public virtual void Generate(UrlHelper urlHelper, Stream stream, int? id)
        {
            //get all URLs for sitemap
            var sitemapUrls = GenerateUrls(urlHelper);

            //split URLs into separate lists based on the max size 
            var sitemaps = sitemapUrls.Select((url, index) => new { Index = index, Value = url })
                .GroupBy(group => group.Index / maxSitemapUrlNumber).Select(group => group.Select(url => url.Value).ToList()).ToList();

            if (!id.HasValue || id.Value == 0 || id.Value > sitemaps.Count)
            {
                //return a sitemap index file
                using (_writer = new XmlTextWriter(stream, Encoding.UTF8))
                {
                    _writer.Formatting = Formatting.Indented;
                    _writer.WriteStartDocument();
                    _writer.WriteStartElement("sitemapindex");
                    _writer.WriteAttributeString("xmlns", "http://www.sitemaps.org/schemas/sitemap/0.9");
                    _writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
                    _writer.WriteAttributeString("xsi:schemaLocation", "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd");

                    //write URLs of all available sitemaps
                    foreach (var sitemap in sitemaps)
                    {
                        var url = urlHelper.RouteUrl("sitemap.xml", new { Id = (sitemaps.IndexOf(sitemap) + 1) }, GetHttpProtocol());    
                        var location = XmlHelper.XmlEncode(url);

                        _writer.WriteStartElement("sitemap");
                        _writer.WriteElementString("loc", location);
                        _writer.WriteElementString("lastmod", DateTime.UtcNow.ToString(DateFormat));
                        _writer.WriteEndElement();
                    }

                    _writer.WriteEndElement();
                }
            }
            else
            {
                //return a certain numbered sitemap file
                using (_writer = new XmlTextWriter(stream, Encoding.UTF8))
                {
                    _writer.Formatting = Formatting.Indented;
                    _writer.WriteStartDocument();
                    _writer.WriteStartElement("urlset");
                    _writer.WriteAttributeString("xmlns", "http://www.sitemaps.org/schemas/sitemap/0.9");
                    _writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
                    _writer.WriteAttributeString("xsi:schemaLocation", "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd");

                    //write URLs from list to sitemap
                    foreach (var url in sitemaps.ElementAt(id.Value - 1))
                        WriteUrlLocation(url.Location, url.UpdateFrequency, url.UpdatedOn);

                    _writer.WriteEndElement();
                }
            }
        }

        #endregion
    }
}
