﻿//#define SIMULATED

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

using Clifton.ExtensionMethods;
using Clifton.Tools.Strings.Extensions;

using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace FeedReaderReceptor
{
	public class FeedReader : BaseReceptor
    {
		public override string Name { get { return "Feed Reader"; } }
		public override string Subname { get { return FeedName; } }
		public override bool IsEdgeReceptor { get { return true; } }
		public override string ConfigurationUI { get { return "FeedReaderConfig.xml"; } }

		[UserConfigurableProperty("Feed URL:")]
		public string FeedUrl { get; set; }

		[UserConfigurableProperty("Feed Name:")]
		public string FeedName {get;set;}

		protected SyndicationFeed feed;

		public FeedReader(IReceptorSystem rsys)
			: base(rsys)
		{
			AddReceiveProtocol("RSSFeedUrl", (Action<dynamic>)(s => ProcessUrl(s)));
			AddEmitProtocol("RSSFeedItem");
			AddEmitProtocol("Exception");
		}

		/// <summary>
		/// If specified, immmediately acquire the feed and start emitting feed items.
		/// </summary>
		public override void EndSystemInit()
		{
			base.EndSystemInit();
			AcquireFeed();
		}

		/// <summary>
		/// When the user configuration fields have been updated, re-acquire the feed.
		/// </summary>
		public override bool UserConfigurationUpdated()
		{
			base.UserConfigurationUpdated();
			AcquireFeed();

			return true;
		}

		protected async void ProcessUrl(dynamic signal)
		{
			string feedUrl = signal.FeedUrl.Value;
			int numItems = signal.MaxItems;
			string tag = signal.Tag;

			try
			{
				SyndicationFeed feed = await GetFeedAsync(feedUrl);
				EmitFeedItems(feed, numItems, tag);
			}
			catch (Exception ex)
			{
				EmitException(ex);
			}
		}

		/// <summary>
		/// Acquire the feed and emit the feed items. 
		/// </summary>
		protected async void AcquireFeed()
		{
			if (!String.IsNullOrEmpty(FeedUrl))
			{
				try
				{
					SyndicationFeed feed = await GetFeedAsync(FeedUrl);
					EmitFeedItems(feed);
				}
				catch (Exception ex)
				{
					EmitException(ex);
				}
			}
		}

		/// <summary>
		/// Acquire the feed asynchronously.
		/// </summary>
		protected async Task<SyndicationFeed> GetFeedAsync(string feedUrl)
		{
#if SIMULATED
			return null;
#else
			SyndicationFeed feed = await Task.Run(() =>
				{
					XmlReader xr = XmlReader.Create(feedUrl);
					SyndicationFeed sfeed = SyndicationFeed.Load(xr);
					xr.Close();

					return sfeed;
				});

			return feed;
#endif
		}

		/// <summary>
		/// Emits only new feed items for display.
		/// </summary>
		protected void EmitFeedItems(SyndicationFeed feed, int maxItems = Int32.MaxValue, string tag = "")
		{
#if SIMULATED
			CreateCarrier("RSSFeedItem", signal =>
				{
					signal.FeedName = FeedName;
					signal.Title = "Test Title";
					signal.URL.Value = "http://test";
					signal.Description = "Test Description";
					signal.Authors = "";
					signal.Categories = "";
					signal.PubDate = DateTime.Now;
					signal.Tag = tag;
					signal.MofN.M = 1;
					signal.MofN.N = 1;
				});
#else
			// Allow -1 to also represent max items.
			int max = (maxItems == -1 ? feed.Items.Count() : maxItems);
			max = Math.Min(max, feed.Items.Count());		// Which ever is less.

			feed.Items.ForEachWithIndexOrUntil((item, idx) =>
				{
					CreateCarrier("RSSFeedItem", signal =>
						{
							signal.FeedName = FeedName;
							signal.Title = item.Title.Text;
							signal.URL.Value = item.Links[0].Uri.ToString();
							signal.Description = item.Summary.Text;
							signal.Authors = String.Join(", ", item.Authors.Select(a => a.Name).ToArray());
							signal.Categories = String.Join(", ", item.Categories.Select(c => c.Name).ToArray());
							signal.PubDate = item.PublishDate.LocalDateTime;
							signal.Tag = tag;
							signal.MofN.M = idx + 1;
							signal.MofN.N = max;
						});
				}, ((item, idx) => idx >= max));
#endif
		}
/*
		protected void EmitFeedItemUrl(SyndicationFeed feed, string feedItemID)
		{
			SyndicationItem item = feed.Items.Single(i => i.Id == feedItemID);
			// Anyone interested directly in the URL (like the NLP) can have a go at it right now.
			// The URL receptor, for example, would open the page on the browser.
			CreateCarrierIfReceiver("URL", signal => signal.Value = item.Links[0].Uri.ToString());
		}
*/
    }
}
