//-----------------------------------------------------------------------
// <copyright file="HttpContextAdapter.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Web;
using log4net;

namespace Raven.Http.Abstractions
{
	public class HttpContextAdapter : IHttpContext
	{
		private readonly HttpContext context;
		private readonly HttpRequestAdapter request;
		private readonly HttpResponseAdapter response;
        private readonly IRavenHttpConfiguration configuration;

		private static readonly Regex maxAgeFinder = new Regex(@"max-age \s* = \s* (\d+)",
		                                                       RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase |
		                                                       RegexOptions.Compiled);
        public HttpContextAdapter(HttpContext context, IRavenHttpConfiguration configuration)
		{
			this.context = context;
			this.configuration = configuration;
			request = new HttpRequestAdapter(context.Request);
			response = new HttpResponseAdapter(context.Response);

        	SetMaxAge();
		}

		private void SetMaxAge()
		{
			string cacheControl = context.Request.Headers["Cache-Control"];
			if (string.IsNullOrEmpty(cacheControl))
				return;

			var match = maxAgeFinder.Match(cacheControl);
			if (match.Success == false) 
				return;

			int timeInSeconds;
			if (int.TryParse(match.Groups[1].Value, out timeInSeconds) == false)
				return;

			context.Response.Cache.SetMaxAge(TimeSpan.FromSeconds(timeInSeconds));
		}

		public IRavenHttpConfiguration Configuration
		{
			get { return configuration; }
		}

		public IHttpRequest Request
		{
			get { return request; }
		}

		public IHttpResponse Response
		{
			get { return response; }
		}

		public IPrincipal User
		{
			get { return context.User; }
		}

		public void FinalizeResonse()
		{
			
		}

		public void SetResponseFilter(Func<Stream, Stream> responseFilter)
		{
			context.Response.Filter = responseFilter(context.Response.Filter);
		}

		private readonly List<Action<ILog>> loggedMessages = new List<Action<ILog>>();
		public void OutputSavedLogItems(ILog logger)
		{
			foreach (var loggedMessage in loggedMessages)
			{
				loggedMessage(logger);
			}
		}

		public void Log(Action<ILog> loggingAction)
		{
			loggedMessages.Add(loggingAction);
		}
	}
}
