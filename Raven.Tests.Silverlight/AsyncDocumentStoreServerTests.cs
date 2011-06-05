﻿using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Json.Linq;

namespace Raven.Tests.Silverlight
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using Client.Document;
	using Client.Extensions;
	using Document;
	using Microsoft.Silverlight.Testing;
	using Microsoft.VisualStudio.TestTools.UnitTesting;

	public class AsyncDocumentStoreServerTests : RavenTestBase
	{
		[Asynchronous]
		public IEnumerable<Task> Can_insert_async_and_multi_get_async()
		{
			var dbname = GenerateNewDatabaseName();
			var documentStore = new DocumentStore {Url = Url + Port};
			documentStore.Initialize();
			yield return documentStore.AsyncDatabaseCommands.EnsureDatabaseExistsAsync(dbname);

			var entity1 = new Company {Name = "Async Company #1"};
			var entity2 = new Company {Name = "Async Company #2"};
			using (var session_for_storing = documentStore.OpenAsyncSession(dbname))
			{
				session_for_storing.Store(entity1);
				session_for_storing.Store(entity2);
				yield return session_for_storing.SaveChangesAsync();
			}

			using (var session_for_loading = documentStore.OpenAsyncSession(dbname))
			{
				var task = session_for_loading.LoadAsync<Company>(new[] {entity1.Id, entity2.Id});
				yield return task;

				Assert.AreEqual(entity1.Name, task.Result[0].Name);
				Assert.AreEqual(entity2.Name, task.Result[1].Name);
			}
		}

		[Asynchronous]
		public IEnumerable<Task> Can_insert_async_and_load_async()
		{
			var dbname = GenerateNewDatabaseName();
			var documentStore = new DocumentStore {Url = Url + Port};
			documentStore.Initialize();
			yield return documentStore.AsyncDatabaseCommands.EnsureDatabaseExistsAsync(dbname);

			var entity = new Company {Name = "Async Company #1"};
			using (var session_for_storing = documentStore.OpenAsyncSession(dbname))
			{
				session_for_storing.Store(entity);
				yield return session_for_storing.SaveChangesAsync();
			}

			using (var session_for_loading = documentStore.OpenAsyncSession(dbname))
			{
				var task = session_for_loading.LoadAsync<Company>(entity.Id);
				yield return task;

				Assert.AreEqual(entity.Name, task.Result.Name);
			}
		}

		[Asynchronous]
		public IEnumerable<Task> Can_insert_async_and_delete_async()
		{
			var dbname = GenerateNewDatabaseName();
			var documentStore = new DocumentStore {Url = Url + Port};
			documentStore.Initialize();
			yield return documentStore.AsyncDatabaseCommands.EnsureDatabaseExistsAsync(dbname);

			var entity = new Company {Name = "Async Company #1", Id = "companies/1"};
			using (var session = documentStore.OpenAsyncSession(dbname))
			{
				session.Store(entity);
				yield return session.SaveChangesAsync();
			}

			using (var for_loading = documentStore.OpenAsyncSession(dbname))
			{
				var loading = for_loading.LoadAsync<Company>(entity.Id);
				yield return loading;
				Assert.IsNotNull(loading.Result);
			}

			using (var for_deleting = documentStore.OpenAsyncSession(dbname))
			{
				var loading = for_deleting.LoadAsync<Company>(entity.Id);
				yield return loading;

				for_deleting.Delete(loading.Result);
				yield return for_deleting.SaveChangesAsync();
			}

			using (var for_verifying = documentStore.OpenAsyncSession(dbname))
			{
				var verification = for_verifying.LoadAsync<Company>(entity.Id);
				yield return verification;

				Assert.IsNull(verification.Result);
			}
		}

		[Asynchronous]
		public IEnumerable<Task> Can_query_by_index()
		{
			var dbname = GenerateNewDatabaseName();
			var documentStore = new DocumentStore {Url = Url + Port};
			documentStore.Initialize();
			yield return documentStore.AsyncDatabaseCommands.EnsureDatabaseExistsAsync(dbname);

			var entity = new Company {Name = "Async Company #1", Id = "companies/1"};
			using (var session = documentStore.OpenAsyncSession(dbname))
			{
				session.Store(entity);
				yield return (session.SaveChangesAsync());
			}

			var task = documentStore.AsyncDatabaseCommands
				.ForDatabase(dbname)
				.PutIndexAsync("Test", new IndexDefinition
				                       	{
				                       		Map =
				                       			"from doc in docs.Companies select new { doc.Name }"
				                       	}, true);
			yield return (task);

			Task<QueryResult> query = null;
			for (int i = 0; i < 50; i++)
			{

				query = documentStore.AsyncDatabaseCommands
					.ForDatabase(dbname)
					.QueryAsync("Test", new IndexQuery(), null);
				yield return (query);
				if (query.Result.IsStale)
				{
					yield return Delay(100);
					continue;
				}
				Assert.AreNotEqual(0, query.Result.TotalResults);
				yield break;
			}
		}

		[Asynchronous]
		public IEnumerable<Task> Can_project_value_from_collection()
		{
			var dbname = GenerateNewDatabaseName();
			var documentStore = new DocumentStore {Url = Url + Port};
			documentStore.Initialize();
			yield return documentStore.AsyncDatabaseCommands.EnsureDatabaseExistsAsync(dbname);

			using (var session = documentStore.OpenAsyncSession(dbname))
			{
				session.Store(new Company
				              	{
				              		Name = "Project Value Company",
				              		Contacts = new List<Contact>
				              		           	{
				              		           		new Contact {Surname = "Abbot"},
				              		           		new Contact {Surname = "Costello"}
				              		           	}
				              	});
				yield return session.SaveChangesAsync();

				
				Task<QueryResult> query;
				do
				{
					query = documentStore.AsyncDatabaseCommands
						.ForDatabase(dbname)
						.QueryAsync("dynamic",
						            new IndexQuery
						            {
						            	FieldsToFetch = new[] {"Contacts,Surname"}
						            },
						            new string[0]);
					yield return query;
					if (query.Result.IsStale)
						yield return Delay(100);
				} while (query.Result.IsStale);
				var ravenJToken = (RavenJArray)query.Result.Results[0]["Contacts"];
				Assert.AreEqual(2, ravenJToken.Count());
				Assert.AreEqual("Abbot", ravenJToken[0].Value<string>("Surname"));
				Assert.AreEqual("Costello", ravenJToken[1].Value<string>("Surname"));
			}
		}
	}
}