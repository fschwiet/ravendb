#if !NET_3_5

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using Raven.Client.Document;

namespace Raven.Client.Connection.Profiling
{
	/// <summary>
	/// Manages all profiling activities for a given item
	/// </summary>
	public class ProfilingContext
	{
		private readonly ConcurrentLruLSet<ProfilingInformation> leastRecentlyUsedCache = new ConcurrentLruLSet<ProfilingInformation>(NumberOfSessionsToTrack);

		private const int NumberOfSessionsToTrack = 50;

		/// <summary>
		/// Register the action as associated with <param name="sender"/>
		/// </summary>
		public void RecordAction(object sender, RequestResultArgs requestResultArgs)
		{
			var profilingInformationHolder = sender as IHoldProfilingInformation;
			if (profilingInformationHolder == null)
				return;

			profilingInformationHolder.ProfilingInformation.Requests.Add(requestResultArgs);

			leastRecentlyUsedCache.Push(profilingInformationHolder.ProfilingInformation);
		}

		/// <summary>
		/// Try to get a session matching the specified id.
		/// </summary>
		public ProfilingInformation TryGet(Guid id)
		{
			return leastRecentlyUsedCache.FirstOrDefault(x => x.Id == id);
		}
	}
}

#endif