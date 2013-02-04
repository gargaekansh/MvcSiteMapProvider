﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using MvcSiteMapProvider.Core.Security;
using MvcSiteMapProvider.Core.Mvc;
using MvcSiteMapProvider.Core.SiteMap.Builder;

namespace MvcSiteMapProvider.Core.SiteMap
{
    /// <summary>
    /// This class acts as the root of a SiteMap object graph and maintains a map
    /// between the child <see cref="T:MvcSiteMapProvider.Core.SiteMapNode"/> nodes.
    /// </summary>
    /// <remarks>
    /// This class was created by extracting the public intefaces of SiteMapProvider, 
    /// StaticSiteMapProvider, and MvcSiteMapProvider.DefaultSiteMapProvider.
    /// </remarks>
    public class SiteMap : ISiteMap
    {
        public SiteMap(
            ISiteMapBuilder siteMapBuilder,
            IAclModule aclModule, 
            IActionMethodParameterResolver actionMethodParameterResolver,
            IControllerTypeResolver controllerTypeResolver
            )
        {
            if (siteMapBuilder == null)
                throw new ArgumentNullException("siteMapBuilder");
            if (aclModule == null)
                throw new ArgumentNullException("aclModule");
            if (actionMethodParameterResolver == null)
                throw new ArgumentNullException("actionMethodParameterResolver");
            if (controllerTypeResolver == null)
                throw new ArgumentNullException("controllerTypeResolver");

            this.siteMapBuilder = siteMapBuilder;
            this.aclModule = aclModule;
            this.actionMethodParameterResolver = actionMethodParameterResolver;
            this.controllerTypeResolver = controllerTypeResolver;

            // TODO: move request caching to a different class that wraps this one
            this.instanceId = Guid.NewGuid();
            this.aclCacheItemKey = "__MVCSITEMAP_ACL_" + this.instanceId.ToString();
            this.currentNodeCacheKey = "__MVCSITEMAP_CN_" + this.instanceId.ToString();
        }

        private readonly ISiteMapBuilder siteMapBuilder;
        private readonly IAclModule aclModule;
        private readonly IActionMethodParameterResolver actionMethodParameterResolver;
        private readonly IControllerTypeResolver controllerTypeResolver;

        private readonly Guid instanceId;

        #region SiteMapProvider state

        private bool enableLocalization;
        //private SiteMap _parentProvider;
        private string resourceKey;
        //private SiteMap _rootProvider;
        private bool securityTrimmingEnabled;

        #endregion

        #region StaticSiteMapProvider state

        private Hashtable childNodeCollectionTable;
        private Hashtable keyTable;
        private Hashtable parentNodeTable;
        private Hashtable urlTable;

        #endregion

        #region DefaultSiteMapProvider state

        protected readonly object synclock = new object();
        protected string aclCacheItemKey;
        protected string currentNodeCacheKey;
        protected ISiteMapNode root;

        #endregion



        #region ISiteMap Members

        /// <summary>
        /// Adds a <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object to the node collection that is maintained by the site map provider.
        /// </summary>
        /// <param name="node">The <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> to add to the node collection maintained by the provider.</param>
        public virtual void AddNode(ISiteMapNode node)
        {
            this.AddNode(node, null);
        }

        /// <summary>
        /// Adds a <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> to the collections that are maintained by the site map provider and establishes a 
        /// parent/child relationship between the <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> objects.
        /// </summary>
        /// <param name="node">The <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> to add to the site map provider.</param>
        /// <param name="parentNode">The <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> under which to add <paramref name="node"/>.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="node"/> is null.
        /// </exception>
        /// <exception cref="T:System.InvalidOperationException">
        /// The <see cref="P:System.Web.SiteMapNode.Url"/> or <see cref="P:System.Web.SiteMapNode.Key"/> is already registered with 
        /// the <see cref="T:System.Web.StaticSiteMapProvider"/>. A site map node must be made up of pages with unique URLs or keys.
        /// </exception>
        public virtual void AddNode(ISiteMapNode node, ISiteMapNode parentNode)
        {
            //if (SiteMapProviderEventHandler.OnAddingSiteMapNode(new SiteMapProviderEventContext(this, node, root)))
            //{

            // TODO: Investigate why this could be the case - perhaps the clear or remove
            // method needs attention instead. This will go into an endless loop when building
            // a sitemap, so we can't do this here.
            // Avoid issue with url table not clearing correctly.
            if (this.FindSiteMapNode(node.Url) != null)
            {
                this.RemoveNode(node);
            }

            //// Allow for external URLs
            //var encoded = UrlPath.EncodeExternalUrl(node);

            // Add the node
            try
            {
                AddNodeInternal(node, parentNode);
            }
            catch
            {
                if (parentNode != null) this.RemoveNode(parentNode);
                AddNodeInternal(node, parentNode);
            }

            //// Restore the external URL
            //if (encoded)
            //{
            //    UrlPath.DecodeExternalUrl(node);
            //}

            //    SiteMapProviderEventHandler.OnAddedSiteMapNode(new SiteMapProviderEventContext(this, node, root));
            //}
        }

        protected virtual void AddNodeInternal(ISiteMapNode node, ISiteMapNode parentNode)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            lock (this.synclock)
            {
                bool flag = false;
                string url = node.Url;
                if (!string.IsNullOrEmpty(url))
                {
                    if (url.StartsWith("http") || url.StartsWith("ftp"))
                    {
                        // This is an external url, so we will encode it
                        url = HttpUtility.UrlEncode(url);
                    }
                    if (HttpRuntime.AppDomainAppVirtualPath != null)
                    {
                        if (!UrlPath.IsAbsolutePhysicalPath(url))
                        {
                            url = UrlPath.MakeVirtualPathAppAbsolute(UrlPath.Combine(HttpRuntime.AppDomainAppVirtualPath, url));
                        }
                        if (this.UrlTable[url] != null)
                        {
                            throw new InvalidOperationException(String.Format(Resources.Messages.MultipleNodesWithIdenticalUrl, url));
                        }
                    }
                    flag = true;
                }
                string key = node.Key;
                if (this.KeyTable.Contains(key))
                {
                    throw new InvalidOperationException(String.Format(Resources.Messages.MultipleNodesWithIdenticalKey, key));
                }
                this.KeyTable[key] = node;
                if (flag)
                {
                    this.UrlTable[url] = node;
                }
                if (parentNode != null)
                {
                    this.ParentNodeTable[node] = parentNode;
                    if (this.ChildNodeCollectionTable[parentNode] == null)
                    {
                        this.ChildNodeCollectionTable[parentNode] = new SiteMapNodeCollection();
                    }
                    ((SiteMapNodeCollection)this.ChildNodeCollectionTable[parentNode]).Add(node);
                }
            }
        }

        public virtual void RemoveNode(ISiteMapNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            lock (this.synclock)
            {
                ISiteMapNode node2 = (ISiteMapNode)this.ParentNodeTable[node];
                if (this.ParentNodeTable.Contains(node))
                {
                    this.ParentNodeTable.Remove(node);
                }
                if (node2 != null)
                {
                    SiteMapNodeCollection nodes = (SiteMapNodeCollection)this.ChildNodeCollectionTable[node2];
                    if ((nodes != null) && nodes.Contains(node))
                    {
                        nodes.Remove(node);
                    }
                }
                string url = node.Url;
                if (((url != null) && (url.Length > 0)) && this.UrlTable.Contains(url))
                {
                    this.UrlTable.Remove(url);
                }
                string key = node.Key;
                if (this.KeyTable.Contains(key))
                {
                    this.KeyTable.Remove(key);
                }
            }
        }

        public virtual void Clear()
        {
            lock (this.synclock)
            {
                root = null;
                if (this.childNodeCollectionTable != null)
                {
                    this.childNodeCollectionTable.Clear();
                }
                if (this.urlTable != null)
                {
                    this.urlTable.Clear();
                }
                if (this.parentNodeTable != null)
                {
                    this.parentNodeTable.Clear();
                }
                if (this.keyTable != null)
                {
                    this.keyTable.Clear();
                }
            }
        }


        /// <summary>
        /// Gets the <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object that represents the currently requested page.
        /// </summary>
        /// <returns>A <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> that represents the currently requested page; otherwise, 
        /// null, if the <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> is not found or cannot be returned for the current user.</returns>
        public ISiteMapNode CurrentNode
        {
            get 
            {
                HttpContext current = HttpContext.Current;
                var currentNode = (ISiteMapNode)current.Items[currentNodeCacheKey];
                if (currentNode == null)
                {
                    currentNode = this.FindSiteMapNode(current);
                    currentNode = this.ReturnNodeIfAccessible(currentNode);
                    current.Items[currentNodeCacheKey] = currentNode;
                }
                return currentNode;
            }
        }

        /// <summary>
        /// Gets or sets a Boolean value indicating whether localized values of <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode">SiteMapNode</see> 
        /// attributes are returned.
        /// </summary>
        /// <remarks>
        /// The EnableLocalization property is used for the get accessor of the Title and Description properties, as well as additional 
        /// Attributes properties of a SiteMapNode object.
        /// </remarks>
        public bool EnableLocalization
        {
            get { return this.enableLocalization; }
            set { this.enableLocalization = value; }
        }

        public ISiteMapNode FindSiteMapNode(string rawUrl)
        {
            if (rawUrl == null)
            {
                throw new ArgumentNullException("rawUrl");
            }
            rawUrl = rawUrl.Trim();
            if (rawUrl.Length == 0)
            {
                return null;
            }
            if (UrlPath.IsAppRelativePath(rawUrl))
            {
                rawUrl = UrlPath.MakeVirtualPathAppAbsolute(rawUrl);
            }
            return this.ReturnNodeIfAccessible((ISiteMapNode)this.UrlTable[rawUrl]);
        }

        /// <summary>
        /// Retrieves a <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object that represents the currently requested page using the specified <see cref="T:System.Web.HttpContext"/> object.
        /// </summary>
        /// <param name="context">The <see cref="T:System.Web.HttpContext"/> used to match node information with the URL of the requested page.</param>
        /// <returns>
        /// A <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> that represents the currently requested page; otherwise, null, if no corresponding <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> can be found in the <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> or if the page context is null.
        /// </returns>
        public ISiteMapNode FindSiteMapNode(System.Web.HttpContext context)
        {
            var httpContext = new HttpContext2(context);
            var routeData = RouteTable.Routes.GetRouteData(httpContext);

            var currentNode = FindSiteMapNode(HttpContext.Current, routeData);
            if (HttpContext.Current.Items[currentNodeCacheKey] == null && currentNode != null)
            {
                HttpContext.Current.Items[currentNodeCacheKey] = currentNode;
            }
            return currentNode;
        }

        public ISiteMapNode FindSiteMapNodeFromKey(string key)
        {
            ISiteMapNode node = this.FindSiteMapNode(key);
            if (node == null)
            {
                node = (ISiteMapNode)this.KeyTable[key];
            }
            return this.ReturnNodeIfAccessible(node);

            //SiteMapNode node = this.FindSiteMapNode(key);
            //if (node == null)
            //{
            //    foreach (SiteMapProvider provider in this.ChildProviderList)
            //    {
            //        this.EnsureChildSiteMapProviderUpToDate(provider);
            //        node = provider.FindSiteMapNodeFromKey(key);
            //        if (node != null)
            //        {
            //            return node;
            //        }
            //    }
            //}
            //return node;

        }

        public SiteMapNodeCollection GetChildNodes(ISiteMapNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            SiteMapNodeCollection collection = (SiteMapNodeCollection)this.ChildNodeCollectionTable[node];
            if (collection == null)
            {
                var node2 = (ISiteMapNode)this.KeyTable[node.Key];
                if (node2 != null)
                {
                    collection = (SiteMapNodeCollection)this.ChildNodeCollectionTable[node2];
                }
            }
            if (collection == null)
            {
                return SiteMapNodeCollection.Empty;
            }
            if (!this.SecurityTrimmingEnabled)
            {
                return SiteMapNodeCollection.ReadOnly(collection);
            }
            HttpContext current = HttpContext.Current;
            SiteMapNodeCollection nodes2 = new SiteMapNodeCollection(collection.Count);
            foreach (ISiteMapNode node3 in collection)
            {
                if (node3.IsAccessibleToUser(current))
                {
                    nodes2.Add(node3);
                }
            }
            return SiteMapNodeCollection.ReadOnly(nodes2);
        }

        public ISiteMapNode GetCurrentNodeAndHintAncestorNodes(int upLevel)
        {
            if (upLevel < -1)
            {
                throw new ArgumentOutOfRangeException("upLevel");
            }
            return this.CurrentNode;

        }

        public ISiteMapNode GetCurrentNodeAndHintNeighborhoodNodes(int upLevel, int downLevel)
        {
            if (upLevel < -1)
            {
                throw new ArgumentOutOfRangeException("upLevel");
            }
            if (downLevel < -1)
            {
                throw new ArgumentOutOfRangeException("downLevel");
            }
            return this.CurrentNode;

        }

        public ISiteMapNode GetParentNode(ISiteMapNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            var parentNode = (ISiteMapNode)this.ParentNodeTable[node];
            if (parentNode == null)
            {
                var node3 = (ISiteMapNode)this.KeyTable[node.Key];
                if (node3 != null)
                {
                    parentNode = (ISiteMapNode)this.ParentNodeTable[node3];
                }
            }
            //if ((parentNode == null) && (this.ParentProvider != null))
            //{
            //    parentNode = this.ParentProvider.GetParentNode(node);
            //}
            return this.ReturnNodeIfAccessible(parentNode);
        }

        public ISiteMapNode GetParentNodeRelativeToCurrentNodeAndHintDownFromParent(int walkupLevels, int relativeDepthFromWalkup)
        {
            if (walkupLevels < 0)
            {
                throw new ArgumentOutOfRangeException("walkupLevels");
            }
            if (relativeDepthFromWalkup < 0)
            {
                throw new ArgumentOutOfRangeException("relativeDepthFromWalkup");
            }
            var currentNodeAndHintAncestorNodes = this.GetCurrentNodeAndHintAncestorNodes(walkupLevels);
            if (currentNodeAndHintAncestorNodes == null)
            {
                return null;
            }
            var parentNodesInternal = this.GetParentNodesInternal(currentNodeAndHintAncestorNodes, walkupLevels);
            if (parentNodesInternal == null)
            {
                return null;
            }
            this.HintNeighborhoodNodes(parentNodesInternal, 0, relativeDepthFromWalkup);
            return parentNodesInternal;

        }

        public ISiteMapNode GetParentNodeRelativeToNodeAndHintDownFromParent(ISiteMapNode node, int walkupLevels, int relativeDepthFromWalkup)
        {
            if (walkupLevels < 0)
            {
                throw new ArgumentOutOfRangeException("walkupLevels");
            }
            if (relativeDepthFromWalkup < 0)
            {
                throw new ArgumentOutOfRangeException("relativeDepthFromWalkup");
            }
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            this.HintAncestorNodes(node, walkupLevels);
            var parentNodesInternal = this.GetParentNodesInternal(node, walkupLevels);
            if (parentNodesInternal == null)
            {
                return null;
            }
            this.HintNeighborhoodNodes(parentNodesInternal, 0, relativeDepthFromWalkup);
            return parentNodesInternal;
        }

        public void HintAncestorNodes(ISiteMapNode node, int upLevel)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            if (upLevel < -1)
            {
                throw new ArgumentOutOfRangeException("upLevel");
            }
        }

        public void HintNeighborhoodNodes(ISiteMapNode node, int upLevel, int downLevel)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            if (upLevel < -1)
            {
                throw new ArgumentOutOfRangeException("upLevel");
            }
            if (downLevel < -1)
            {
                throw new ArgumentOutOfRangeException("downLevel");
            }

        }

        /// <summary>
        /// Retrieves a Boolean value indicating whether the specified <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object can be viewed by the user in the specified context.
        /// </summary>
        /// <param name="context">The <see cref="T:System.Web.HttpContext"/> that contains user information.</param>
        /// <param name="node">The <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> that is requested by the user.</param>
        /// <returns>
        /// true if security trimming is enabled and <paramref name="node"/> can be viewed by the user or security trimming is not enabled; otherwise, false.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="context"/> is null.
        /// - or -
        /// <paramref name="node"/> is null.
        /// </exception>
        public bool IsAccessibleToUser(System.Web.HttpContext context, ISiteMapNode node)
        {
            if (!SecurityTrimmingEnabled)
            {
                return true;
            }

            // Construct call cache?
            if (context.Items[aclCacheItemKey] == null)
            {
                context.Items[aclCacheItemKey] = new Dictionary<ISiteMapNode, bool>();
            }
            Dictionary<ISiteMapNode, bool> aclCacheItem
                = (Dictionary<ISiteMapNode, bool>)context.Items[aclCacheItemKey];

            // Is the result of this call cached?
            if (!aclCacheItem.ContainsKey(node))
            {
                aclCacheItem[node] = aclModule.IsAccessibleToUser(controllerTypeResolver, this, context, node);
            }
            return aclCacheItem[node];
        }

        //public SiteMap ParentProvider
        //{
        //    get
        //    {
        //        return this._parentProvider;

        //    }
        //    set
        //    {
        //        this._parentProvider = value;

        //    }
        //}

        /// <summary>
        /// Get or sets the resource key that is used for localizing <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> attributes. 
        /// </summary>
        /// <remarks>
        /// The ResourceKey property is used with the GetImplicitResourceString method of the <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> class. 
        /// For the Title and Description properties, as well as any additional attributes that are defined in the Attributes collection of the 
        /// <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object, the GetImplicitResourceString method takes precedence over the 
        /// GetExplicitResourceString when the localization is enabled with the EnableLocalization property set to true. 
        /// </remarks>
        public string ResourceKey
        {
            get { return this.resourceKey; }
            set { this.resourceKey = value; }
        }

        /// <summary>
        /// Gets the root <see cref="T:MvcSiteMapProvider.Core.SiteMap.SiteMapNode"/> object of the site map data that the current provider represents.
        /// </summary>
        public ISiteMapNode RootNode
        {
            get { return this.ReturnNodeIfAccessible(root); }
        }

        /// <summary>
        /// Gets a Boolean value indicating whether a site map provider filters site map nodes based on a user's role.
        /// </summary>
        public bool SecurityTrimmingEnabled
        {
            get { return this.securityTrimmingEnabled; }
            set 
            {
                if (value == false && this.securityTrimmingEnabled == true)
                    throw new System.Security.SecurityException(Resources.Messages.SecurityTrimmingCannotBeDisabled);
                this.securityTrimmingEnabled = value; 
            }
        }

        public ISiteMapNode BuildSiteMap()
        {
            // Return immediately if this method has been called before
            if (root != null) 
            {
                return root;
            }
            lock (this.synclock)
            {
                // Return immediately if the prevous lock called this before
                if (root != null)
                {
                    return root;
                }
                root = siteMapBuilder.BuildSiteMap(this, root);
                return root;
            }
        }

        /// <summary>
        /// Finds the site map node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public ISiteMapNode FindSiteMapNode(ControllerContext context)
        {
            return FindSiteMapNode(HttpContext.Current, context.RouteData);
        }

        /// <summary>
        /// Finds the site map node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="routeData">The route data.</param>
        /// <returns></returns>
        private ISiteMapNode FindSiteMapNode(HttpContext context, RouteData routeData)
        {
            // Node
            ISiteMapNode node = null;

            // Fetch route data
            var httpContext = new HttpContext2(context);
            if (routeData != null)
            {
                RequestContext requestContext = new RequestContext(httpContext, routeData);
                VirtualPathData vpd = routeData.Route.GetVirtualPath(
                    requestContext, routeData.Values);
                string appPathPrefix = (requestContext.HttpContext.Request.ApplicationPath
                    ?? string.Empty).TrimEnd('/') + "/";
                node = this.FindSiteMapNode(httpContext.Request.Path) as ISiteMapNode;

                if (!routeData.Values.ContainsKey("area"))
                {
                    if (routeData.DataTokens["area"] != null)
                    {
                        routeData.Values.Add("area", routeData.DataTokens["area"]);
                    }
                    else
                    {
                        routeData.Values.Add("area", "");
                    }
                }

                ISiteMapNode mvcNode = node as ISiteMapNode;
                if (mvcNode == null || routeData.Route != RouteTable.Routes[mvcNode.Route])
                {
                    if (NodeMatchesRoute(RootNode as ISiteMapNode, routeData.Values))
                    {
                        node = RootNode;
                    }
                }

                if (node == null)
                {
                    node = FindControllerActionNode(RootNode, routeData.Values, routeData.Route);
                }
            }

            // Try base class
            if (node == null)
            {
                node = this.FindSiteMapNode(context);
            }

            // Check accessibility
            if (node != null)
            {
                //if (node.IsAccessibleToUser(context))
                if (this.IsAccessibleToUser(context, node))
                {
                    return node;
                }
            }
            return null;
        }

        #endregion

        #region Private Members 

        /// <summary>
        /// Finds the controller action node.
        /// </summary>
        /// <param name="rootNode">The root node.</param>
        /// <param name="values">The values.</param>
        /// <param name="route">The route.</param>
        /// <returns>
        /// A controller action node represented as a <see cref="SiteMapNode"/> instance
        /// </returns>
        private ISiteMapNode FindControllerActionNode(ISiteMapNode rootNode, IDictionary<string, object> values, RouteBase route)
        {
            if (rootNode != null)
            {
                // Get all child nodes
                SiteMapNodeCollection childNodes = GetChildNodes(rootNode);

                // Search current level
                foreach (ISiteMapNode node in childNodes)
                {
                    // Check if it is an MvcSiteMapNode
                    var mvcNode = node as ISiteMapNode;
                    if (mvcNode != null)
                    {
                        // Look at the route property
                        if (!string.IsNullOrEmpty(mvcNode.Route))
                        {
                            if (RouteTable.Routes[mvcNode.Route] == route)
                            {
                                // This looks a bit weird, but if i set up a node to a general route ie /Controller/Action/ID
                                // I need to check that the values are the same so that it doesn't swallow all of the nodes that also use that same general route
                                if (NodeMatchesRoute(mvcNode, values))
                                {
                                    return mvcNode;
                                }
                            }
                        }
                        else if (NodeMatchesRoute(mvcNode, values))
                        {
                            return mvcNode;
                        }
                    }
                }

                // Search one deeper level
                foreach (ISiteMapNode node in childNodes)
                {
                    var siteMapNode = FindControllerActionNode(node, values, route);
                    if (siteMapNode != null)
                    {
                        return siteMapNode;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Nodes the matches route.
        /// </summary>
        /// <param name="mvcNode">The MVC node.</param>
        /// <param name="values">The values.</param>
        /// <returns>
        /// A matches route represented as a <see cref="bool"/> instance 
        /// </returns>
        private bool NodeMatchesRoute(ISiteMapNode mvcNode, IDictionary<string, object> values)
        {
            var nodeValid = true;

            if (mvcNode != null)
            {
                // Find action method parameters?
                IEnumerable<string> actionParameters = new List<string>();
                //if (mvcNode.DynamicNodeProvider == null && mvcNode.IsDynamic == false)
                if (mvcNode.IsDynamic == false)
                {
                    actionParameters = actionMethodParameterResolver.ResolveActionMethodParameters(
                        controllerTypeResolver, mvcNode.Area, mvcNode.Controller, mvcNode.Action);
                }

                // Verify route values
                if (values.Count > 0)
                {
                    // Checking for same keys and values.
                    if (!CompareMustMatchRouteValues(mvcNode.RouteValues, values))
                    {
                        return false;
                    }

                    foreach (var pair in values)
                    {
                        if (mvcNode.Attributes.ContainsKey(pair.Key) && !string.IsNullOrEmpty(mvcNode.Attributes[pair.Key]))
                        {
                            if (mvcNode.Attributes[pair.Key].ToLowerInvariant() == pair.Value.ToString().ToLowerInvariant())
                            {
                                continue;
                            }
                            else
                            {
                                // Is the current pair.Key a parameter on the action method?
                                if (!actionParameters.Contains(pair.Key, StringComparer.InvariantCultureIgnoreCase))
                                {
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            if (pair.Value == null || string.IsNullOrEmpty(pair.Value.ToString()) || pair.Value == UrlParameter.Optional)
                            {
                                continue;
                            }
                            else if (pair.Key == "area")
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            else
            {
                nodeValid = false;
            }

            return nodeValid;
        }

        ///// <summary>
        ///// Nodes the matches route.
        ///// </summary>
        ///// <param name="mvcNode">The MVC node.</param>
        ///// <param name="values">The values.</param>
        ///// <returns>
        ///// A matches route represented as a <see cref="bool"/> instance 
        ///// </returns>
        //private bool NodeMatchesRoute(ISiteMapNode mvcNode, IDictionary<string, object> values)
        //{
        //    // Temporary Thread Lock to help with debugging
        //    lock (this.synclock)
        //    {

        //        var nodeValid = true;

        //        if (mvcNode != null)
        //        {
        //            // Find action method parameters?
        //            IEnumerable<string> actionParameters = new List<string>();
        //            //if (mvcNode.DynamicNodeProvider == null && mvcNode.IsDynamic == false)
        //            if (mvcNode.IsDynamic == false)
        //            {
        //                actionParameters = actionMethodParameterResolver.ResolveActionMethodParameters(
        //                    controllerTypeResolver, mvcNode.Area, mvcNode.Controller, mvcNode.Action);
        //            }

        //            // Verify route values
        //            if (values.Count > 0)
        //            {
        //                // Checking for same keys and values.
        //                if (!CompareMustMatchRouteValues(mvcNode.RouteValues, values))
        //                {
        //                    return false;
        //                }

        //                foreach (var pair in values)
        //                {
        //                    if (!string.IsNullOrEmpty(mvcNode.Attributes[pair.Key]))
        //                    {
        //                        if (mvcNode.Attributes[pair.Key].ToLowerInvariant() == pair.Value.ToString().ToLowerInvariant())
        //                        {
        //                            continue;
        //                        }
        //                        else
        //                        {
        //                            // Is the current pair.Key a parameter on the action method?
        //                            if (!actionParameters.Contains(pair.Key, StringComparer.InvariantCultureIgnoreCase))
        //                            {
        //                                return false;
        //                            }
        //                        }
        //                    }
        //                    else
        //                    {
        //                        if (pair.Value == null || string.IsNullOrEmpty(pair.Value.ToString()) || pair.Value == UrlParameter.Optional)
        //                        {
        //                            continue;
        //                        }
        //                        else if (pair.Key == "area")
        //                        {
        //                            return false;
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //        else
        //        {
        //            nodeValid = false;
        //        }

        //        return nodeValid;

        //    }
        //}

        /// <summary>
        /// Returns whether the two route value collections have same keys and same values.
        /// </summary>
        /// <param name="mvcNodeRouteValues">The route values of the original node.</param>
        /// <param name="routeValues">The route values to check in the given node.</param>
        /// <returns><c>True</c> if the <paramref name="mvcNodeRouteValues"/> contains all keys and the same values as the given <paramref name="routeValues"/>, otherwise <c>false</c>.</returns>
        private static bool CompareMustMatchRouteValues(IDictionary<string, object> mvcNodeRouteValues, IDictionary<string, object> routeValues)
        {
            var routeKeys = mvcNodeRouteValues.Keys;

            foreach (var pair in routeValues)
            {
                if (routeKeys.Contains(pair.Key) && !mvcNodeRouteValues[pair.Key].ToString().Equals(pair.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }



        private ISiteMapNode GetParentNodesInternal(ISiteMapNode node, int walkupLevels)
        {
            if (walkupLevels > 0)
            {
                do
                {
                    node = node.ParentNode;
                    walkupLevels--;
                }
                while ((node != null) && (walkupLevels != 0));
            }
            return node;
        }

        private ISiteMapNode ReturnNodeIfAccessible(ISiteMapNode node)
        {
            if ((node != null) && node.IsAccessibleToUser(HttpContext.Current))
            {
                return node;
            }
            return null;
        }

        private IDictionary ChildNodeCollectionTable
        {
            get
            {
                if (this.childNodeCollectionTable == null)
                {
                    lock (this.synclock)
                    {
                        if (this.childNodeCollectionTable == null)
                        {
                            this.childNodeCollectionTable = new Hashtable();
                        }
                    }
                }
                return this.childNodeCollectionTable;
            }
        }

        private IDictionary KeyTable
        {
            get
            {
                if (this.keyTable == null)
                {
                    lock (this.synclock)
                    {
                        if (this.keyTable == null)
                        {
                            this.keyTable = new Hashtable();
                        }
                    }
                }
                return this.keyTable;
            }
        }

        private IDictionary ParentNodeTable
        {
            get
            {
                if (this.parentNodeTable == null)
                {
                    lock (this.synclock)
                    {
                        if (this.parentNodeTable == null)
                        {
                            this.parentNodeTable = new Hashtable();
                        }
                    }
                }
                return this.parentNodeTable;
            }
        }

        private IDictionary UrlTable
        {
            get
            {
                if (this.urlTable == null)
                {
                    lock (this.synclock)
                    {
                        if (this.urlTable == null)
                        {
                            this.urlTable = new Hashtable(StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                return this.urlTable;
            }
        }

        #endregion

    }
}