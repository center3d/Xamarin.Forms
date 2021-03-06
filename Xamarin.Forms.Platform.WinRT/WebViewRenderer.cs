﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.WebUI;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;
using Xamarin.Forms.Internals;
using static System.String;

#if WINDOWS_UWP

namespace Xamarin.Forms.Platform.UWP
#else

namespace Xamarin.Forms.Platform.WinRT
#endif
{
	public class WebViewRenderer : ViewRenderer<WebView, Windows.UI.Xaml.Controls.WebView>, IWebViewDelegate
	{
		WebNavigationEvent _eventState;
		bool _updating;
		const string LocalScheme = "ms-appx-web:///";

		// Script to insert a <base> tag into an HTML document
		const string BaseInsertionScript = @"
var head = document.getElementsByTagName('head')[0];
var bases = head.getElementsByTagName('base');
if(bases.length == 0){
    head.innerHTML = 'baseTag' + head.innerHTML;
}";
		IWebViewController ElementController => Element;

		public void LoadHtml(string html, string baseUrl)
		{
			if (IsNullOrEmpty(baseUrl))
			{
				baseUrl = LocalScheme;
			}

			// Generate a base tag for the document
			var baseTag = $"<base href=\"{baseUrl}\"></base>";

			string htmlWithBaseTag;

			// Set up an internal WebView we can use to load and parse the original HTML string
			var internalWebView = new Windows.UI.Xaml.Controls.WebView();

			// When the 'navigation' to the original HTML string is done, we can modify it to include our <base> tag
			internalWebView.NavigationCompleted += async (sender, args) =>
			{
				// Generate a version of the <base> script with the correct <base> tag
				var script = BaseInsertionScript.Replace("baseTag", baseTag);

				// Run it and retrieve the updated HTML from our WebView
				await sender.InvokeScriptAsync("eval", new[] { script });
				htmlWithBaseTag = await sender.InvokeScriptAsync("eval", new[] { "document.documentElement.outerHTML;" });

				// Set the HTML for the 'real' WebView to the updated HTML
				Control.NavigateToString(!IsNullOrEmpty(htmlWithBaseTag) ? htmlWithBaseTag : html);
			};

			// Kick off the initial navigation
			internalWebView.NavigateToString(html);
		}

		public void LoadUrl(string url)
		{
			Uri uri = new Uri(url, UriKind.RelativeOrAbsolute);

			if (!uri.IsAbsoluteUri)
			{
				uri = new Uri(LocalScheme +  url, UriKind.RelativeOrAbsolute);
			}

			Control.Source = uri;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (Control != null)
				{
					Control.NavigationStarting -= OnNavigationStarted;
					Control.NavigationCompleted -= OnNavigationCompleted;
					Control.NavigationFailed -= OnNavigationFailed;
				}
			}

			base.Dispose(disposing);
		}

		protected override void OnElementChanged(ElementChangedEventArgs<WebView> e)
		{
			base.OnElementChanged(e);

			if (e.OldElement != null)
			{
				var oldElementController = e.OldElement as IWebViewController;
				oldElementController.EvalRequested -= OnEvalRequested;
				oldElementController.GoBackRequested -= OnGoBackRequested;
				oldElementController.GoForwardRequested -= OnGoForwardRequested;
			}

			if (e.NewElement != null)
			{
				if (Control == null)
				{
					var webView = new Windows.UI.Xaml.Controls.WebView();
					webView.NavigationStarting += OnNavigationStarted;
					webView.NavigationCompleted += OnNavigationCompleted;
					webView.NavigationFailed += OnNavigationFailed;
					SetNativeControl(webView);
				}

				var newElementController = e.NewElement as IWebViewController;
				newElementController.EvalRequested += OnEvalRequested;
				newElementController.GoForwardRequested += OnGoForwardRequested;
				newElementController.GoBackRequested += OnGoBackRequested;

				Load();
			}
		}

		protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			base.OnElementPropertyChanged(sender, e);

			if (e.PropertyName == WebView.SourceProperty.PropertyName)
			{
				if (!_updating)
					Load();
			}
		}

		void Load()
		{
			if (Element.Source != null)
				Element.Source.Load(this);

			UpdateCanGoBackForward();
		}

		async void OnEvalRequested(object sender, EvalRequested eventArg)
		{
			await Control.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await Control.InvokeScriptAsync("eval", new[] { eventArg.Script }));
		}

		void OnGoBackRequested(object sender, EventArgs eventArgs)
		{
			if (Control.CanGoBack)
			{
				_eventState = WebNavigationEvent.Back;
				Control.GoBack();
			}

			UpdateCanGoBackForward();
		}

		void OnGoForwardRequested(object sender, EventArgs eventArgs)
		{
			if (Control.CanGoForward)
			{
				_eventState = WebNavigationEvent.Forward;
				Control.GoForward();
			}

			UpdateCanGoBackForward();
		}

		void OnNavigationCompleted(Windows.UI.Xaml.Controls.WebView sender, WebViewNavigationCompletedEventArgs e)
		{
			if (e.Uri != null)
				SendNavigated(new UrlWebViewSource { Url = e.Uri.AbsoluteUri }, _eventState, WebNavigationResult.Success);

			UpdateCanGoBackForward();
		}

		void OnNavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
		{
			if (e.Uri != null)
				SendNavigated(new UrlWebViewSource { Url = e.Uri.AbsoluteUri }, _eventState, WebNavigationResult.Failure);
		}

		void OnNavigationStarted(Windows.UI.Xaml.Controls.WebView sender, WebViewNavigationStartingEventArgs e)
		{
			Uri uri = e.Uri;

			if (uri != null)
			{
				var args = new WebNavigatingEventArgs(_eventState, new UrlWebViewSource { Url = uri.AbsoluteUri }, uri.AbsoluteUri);

				ElementController.SendNavigating(args);
				e.Cancel = args.Cancel;

				// reset in this case because this is the last event we will get
				if (args.Cancel)
					_eventState = WebNavigationEvent.NewPage;
			}
		}

		void SendNavigated(UrlWebViewSource source, WebNavigationEvent evnt, WebNavigationResult result)
		{
			_updating = true;
			((IElementController)Element).SetValueFromRenderer(WebView.SourceProperty, source);
			_updating = false;

			ElementController.SendNavigated(new WebNavigatedEventArgs(evnt, source, source.Url, result));

			UpdateCanGoBackForward();
			_eventState = WebNavigationEvent.NewPage;
		}

		void UpdateCanGoBackForward()
		{
			ElementController.CanGoBack = Control.CanGoBack;
			ElementController.CanGoForward = Control.CanGoForward;
		}
	}
}