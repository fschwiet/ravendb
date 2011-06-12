//-----------------------------------------------------------------------
// <copyright file="AsyncDocumentSession.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
#if !NET_3_5

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Client.Connection.Async;
using Raven.Client.Listeners;

namespace Raven.Client.Document.Async
{
	using Linq;

	/// <summary>
	/// Implementation for async document session 
	/// </summary>
	public class AsyncDocumentSession : InMemoryDocumentSessionOperations, IAsyncDocumentSession, IAsyncAdvancedSessionOperations, IDocumentQueryGenerator
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="AsyncDocumentSession"/> class.
		/// </summary>
		public AsyncDocumentSession(DocumentStore documentStore, 
            IAsyncDatabaseCommands asyncDatabaseCommands, 
            IDocumentQueryListener[] queryListeners, 
            IDocumentStoreListener[] storeListeners, 
            IDocumentDeleteListener[] deleteListeners)
			: base(documentStore, queryListeners, storeListeners, deleteListeners)
		{
			AsyncDatabaseCommands = asyncDatabaseCommands;
		}

		/// <summary>
		/// Gets the async database commands.
		/// </summary>
		/// <value>The async database commands.</value>
		public IAsyncDatabaseCommands AsyncDatabaseCommands { get; private set; }

	    /// <summary>
	    /// Query the specified index using Lucene syntax
	    /// </summary>
	    public IAsyncDocumentQuery<T> AsyncLuceneQuery<T>(string index)
	    {
	        return new AsyncDocumentQuery<T>(this, 
#if !SILVERLIGHT
					null,
#endif
				AsyncDatabaseCommands, index, new string[0], queryListeners);
	    }

	    /// <summary>
	    /// Dynamically query RavenDB using Lucene syntax
	    /// </summary>
		public IAsyncDocumentQuery<T> AsyncLuceneQuery<T>()
	    {
	    	var indexName = "dynamic";
			if (typeof(T) != typeof(object))
			{
				indexName += "/" + Conventions.GetTypeTagName(typeof(T));
			}
            return new AsyncDocumentQuery<T>(this, 
#if !SILVERLIGHT
					null,
#endif
					AsyncDatabaseCommands, indexName, new string[0], queryListeners);
	    }

	    /// <summary>
        /// Get the accessor for advanced operations
        /// </summary>
        /// <remarks>
        /// Those operations are rarely needed, and have been moved to a separate 
        /// property to avoid cluttering the API
        /// </remarks>
	    public IAsyncAdvancedSessionOperations Advanced
	    {
            get { return this; }
	    }

		/// <summary>
		/// Loads the specified entities with the specified id after applying
		/// conventions on the provided id to get the real document id.
		/// </summary>
		/// <remarks>
		/// This method allows you to call:
		/// Load{Post}(1)
		/// And that call will internally be translated to 
		/// Load{Post}("posts/1");
		/// 
		/// Or whatever your conventions specify.
		/// </remarks>
		public Task<T> LoadAsync<T>(ValueType id)
		{
			var documentKey = Conventions.FindFullDocumentKeyFromNonStringIdentifier(id, typeof(T), false);
			return LoadAsync<T>(documentKey);
		}

	    /// <summary>
		/// Begins the async load operation
		/// </summary>
		/// <param name="id">The id.</param>
		/// <returns></returns>
		public Task<T> LoadAsync<T>(string id)
		{
			object entity;
            if (entitiesByKey.TryGetValue(id, out entity))
            {
                var tcs = new TaskCompletionSource<T>();
                tcs.TrySetResult((T)entity);
                return tcs.Task;
            }
			
			IncrementRequestCount();

			return AsyncDatabaseCommands.GetAsync(id)
                .ContinueWith(task =>
                {
                    JsonDocument documentFound;
                    try
                    {
                        documentFound = task.Result;
                    }
                    catch (WebException ex)
                    {
                        var httpWebResponse = ex.Response as HttpWebResponse;
                        if (httpWebResponse != null && httpWebResponse.StatusCode == HttpStatusCode.NotFound)
                            return default(T);
                        throw;
                    }
                    if (documentFound == null)
                        return default(T);

                    return TrackEntity<T>(documentFound);
                });
		}

	    /// <summary>
		/// Begins the async multi load operation
		/// </summary>
		/// <param name="ids">The ids.</param>
		/// <returns></returns>
		public Task<T[]> LoadAsync<T>(string[] ids)
		{
			IncrementRequestCount();
			return AsyncDatabaseCommands.MultiGetAsync(ids)
                .ContinueWith(task => task.Result.Select(TrackEntity<T>).ToArray());
		}

		/// <summary>
		/// Begins the async save changes operation
		/// </summary>
		/// <returns></returns>
		public Task SaveChangesAsync()
		{
			var cachingScope = EntitiesToJsonCachingScope();
			var data = PrepareForSaveChanges();
			return AsyncDatabaseCommands.BatchAsync(data.Commands.ToArray())
				.ContinueWith(task =>
				{
					UpdateBatchResults(task.Result, data.Entities);
					cachingScope.Dispose();
				});
		}

		/// <summary>
        /// Get the json document by key from the store
        /// </summary>
	    protected override JsonDocument GetJsonDocument(string documentKey)
	    {
	        throw new NotSupportedException("Cannot get a document in a syncronous manner using async document session");
	    }

	    /// <summary>
		/// Commits the specified tx id.
		/// </summary>
		/// <param name="txId">The tx id.</param>
		public override void Commit(Guid txId)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Rollbacks the specified tx id.
		/// </summary>
		/// <param name="txId">The tx id.</param>
		public override void Rollback(Guid txId)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Promotes the transaction.
		/// </summary>
		/// <param name="fromTxId">From tx id.</param>
		/// <returns></returns>
		public override byte[] PromoteTransaction(Guid fromTxId)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Dynamically queries RavenDB using LINQ
		/// </summary>
		/// <typeparam name="T">The result of the query</typeparam>
		public IRavenQueryable<T> Query<T>()
		{
			string indexName = "dynamic";
			if (typeof(T) != typeof(object))
			{
				indexName += "/" + Conventions.GetTypeTagName(typeof(T));
			}
			
			var ravenQueryStatistics = new RavenQueryStatistics();

			return new RavenQueryInspector<T>(
				new DynamicRavenQueryProvider<T>(this, indexName, ravenQueryStatistics, 
				#if !SILVERLIGHT
				null,
				#endif
				Advanced.AsyncDatabaseCommands),
				ravenQueryStatistics,
				indexName,
				null,
#if !SILVERLIGHT
 null,
#endif
				Advanced.AsyncDatabaseCommands);
		}

		IRavenQueryable<T> IAsyncDocumentSession.Query<T>(string indexName)
		{
			throw new NotSupportedException();
		}

		IDocumentQuery<T> IDocumentQueryGenerator.Query<T>(string indexName)
		{
			throw new NotSupportedException();
		}

		/// <summary>
		/// Create a new query for <typeparam name="T"/>
		/// </summary>
		public IAsyncDocumentQuery<T> AsyncQuery<T>(string indexName)
		{
			return AsyncLuceneQuery<T>(indexName);
		}
	}
}
#endif