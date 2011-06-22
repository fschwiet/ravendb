//-----------------------------------------------------------------------
// <copyright file="Documents.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Isam.Esent.Interop;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Database;
using Raven.Database.Json;
using Raven.Database.Extensions;
using Raven.Database.Storage;
using Raven.Http;
using Raven.Json.Linq;

namespace Raven.Storage.Esent.StorageActions
{
	public partial class DocumentStorageActions : IDocumentStorageActions, ITransactionStorageActions
	{

		public long GetDocumentsCount()
		{
			if (Api.TryMoveFirst(session, Details))
				return Api.RetrieveColumnAsInt32(session, Details, tableColumnsCache.DetailsColumns["document_count"]).Value;
			return 0;
		}

		public JsonDocument DocumentByKey(string key, TransactionInformation transactionInformation)
		{
			return DocumentByKeyInternal(key, transactionInformation, (metadata, createDocument) =>
			{
				Debug.Assert(metadata.Etag != null);
				return new JsonDocument
				{
					DataAsJson = createDocument(metadata.Key, metadata.Etag.Value, metadata.Metadata),
					Etag = metadata.Etag,
					Key = metadata.Key,
					LastModified = metadata.LastModified,
					Metadata = metadata.Metadata,
					NonAuthoritiveInformation = metadata.NonAuthoritiveInformation,
				};
			});
		}

		public JsonDocumentMetadata DocumentMetadataByKey(string key, TransactionInformation transactionInformation)
		{
			return DocumentByKeyInternal(key, transactionInformation, (metadata, func) => metadata);
		}

		private T DocumentByKeyInternal<T>(string key, TransactionInformation transactionInformation, Func<JsonDocumentMetadata, Func<string, Guid, RavenJObject, RavenJObject>, T> createResult)
			where T : class
		{
			bool existsInTx = IsDocumentModifiedInsideTransaction(key);
			
			if (transactionInformation != null && existsInTx)
			{
				var txId = Api.RetrieveColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["locked_by_transaction"]);
				if (new Guid(txId) == transactionInformation.Id)
				{
					if (Api.RetrieveColumnAsBoolean(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["delete_document"]) == true)
					{
						logger.DebugFormat("Document with key '{0}' was deleted in transaction: {1}", key, transactionInformation.Id);
						return null;
					}
					var etag = Api.RetrieveColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["etag"]).TransfromToGuidWithProperSorting();

					RavenJObject metadata = ReadDocumentMetadataInTransaction(key, etag);


					logger.DebugFormat("Document with key '{0}' was found in transaction: {1}", key, transactionInformation.Id);
					return createResult(new JsonDocumentMetadata()
					{
						NonAuthoritiveInformation = false,// we are the transaction, therefor we are Authoritive
						Etag = etag,
						LastModified = Api.RetrieveColumnAsDateTime(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["last_modified"]).Value,
						Key = Api.RetrieveColumnAsString(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["key"], Encoding.Unicode),
						Metadata = metadata
					}, ReadDocumentDataInTransaction);
				}
			}

			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekEQ) == false)
			{
				if(existsInTx)
				{
					logger.DebugFormat("Committed document with key '{0}' was not found, but exists in a separate transaction", key);
					return createResult(new JsonDocumentMetadata
					{
						Etag = Guid.Empty,
						Key = key,
						Metadata = new RavenJObject{{Constants.RavenDocumentDoesNotExists, true}},
						NonAuthoritiveInformation = true,
						LastModified = DateTime.MinValue,
					}, (docKey, etag, metadata) => new RavenJObject());
				}
				logger.DebugFormat("Document with key '{0}' was not found", key);
				return null;
			}
			var existingEtag = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"]).TransfromToGuidWithProperSorting();
			logger.DebugFormat("Document with key '{0}' was found", key);
			return createResult(new JsonDocumentMetadata()
			{
				Etag = existingEtag,
				NonAuthoritiveInformation = existsInTx,
				LastModified = Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]).Value,
				Key = Api.RetrieveColumnAsString(session, Documents, tableColumnsCache.DocumentsColumns["key"], Encoding.Unicode),
				Metadata = ReadDocumentMetadata(key, existingEtag)
			}, ReadDocumentData);
		}

		private RavenJObject ReadDocumentMetadataInTransaction(string key, Guid etag)
		{
			var cachedDocument = cacher.GetCachedDocument(key, etag);
			if (cachedDocument != null)
			{
				return cachedDocument.Metadata;
			}

			return Api.RetrieveColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["metadata"]).ToJObject();

		}

		private RavenJObject ReadDocumentDataInTransaction(string key, Guid etag, RavenJObject metadata)
		{
			var cachedDocument = cacher.GetCachedDocument(key, etag);
			if (cachedDocument != null)
			{
				return cachedDocument.Document;
			}

			var dataBuffer = Api.RetrieveColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["data"]);
			var data = documentCodecs.Aggregate(dataBuffer, (bytes, codec) => codec.Decode(key, metadata, bytes)).ToJObject();
		    cacher.SetCachedDocument(key, etag, data, metadata);
			return data;
		}

		private RavenJObject ReadDocumentMetadata(string key, Guid existingEtag)
		{
			var existingCachedDocument = cacher.GetCachedDocument(key, existingEtag);
			if (existingCachedDocument != null)
				return existingCachedDocument.Metadata;

			return Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();
		}

		private RavenJObject ReadDocumentData(string key, Guid existingEtag, RavenJObject metadata)
		{
			var existingCachedDocument = cacher.GetCachedDocument(key, existingEtag);
			if (existingCachedDocument != null)
				return existingCachedDocument.Document;

			var dataBuffer = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]);

			var data = documentCodecs.Aggregate(dataBuffer, (bytes, codec) => codec.Decode(key, metadata, bytes)).ToJObject();

			cacher.SetCachedDocument(key, existingEtag, data, metadata);

			return data;
		}

		public IEnumerable<JsonDocument> GetDocumentsByReverseUpdateOrder(int start)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_etag");
			Api.MoveAfterLast(session, Documents);
			for (int i = 0; i < start; i++)
			{
				if (Api.TryMovePrevious(session, Documents) == false)
					yield break;
			}
			while (Api.TryMovePrevious(session, Documents))
			{
				var data = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]);
				var metadata = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();
				var key = Api.RetrieveColumnAsString(session, Documents, tableColumnsCache.DocumentsColumns["key"], Encoding.Unicode);

				data = documentCodecs.Aggregate(data, (bytes, codec) => codec.Decode(key, metadata, bytes));

				yield return new JsonDocument
				{
					Key = key,
					DataAsJson = data.ToJObject(),
					NonAuthoritiveInformation = IsDocumentModifiedInsideTransaction(key),
					LastModified = Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]).Value,
					Etag = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"]).TransfromToGuidWithProperSorting(),
					Metadata = metadata
				};
			}
		}


		public IEnumerable<JsonDocument> GetDocumentsAfter(Guid etag)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_etag");
			Api.MakeKey(session, Documents, etag.TransformToValueForEsentSorting(), MakeKeyGrbit.NewKey);
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekGT) == false)
				yield break;
			do
			{
				var data = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]);
				var key = Api.RetrieveColumnAsString(session, Documents, tableColumnsCache.DocumentsColumns["key"], Encoding.Unicode);
				var metadata = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();

				data = documentCodecs.Aggregate(data, (bytes, codec) => codec.Decode(key, metadata, bytes));

				yield return new JsonDocument
				{
					Key = key,
					DataAsJson = data.ToJObject(),
					NonAuthoritiveInformation = IsDocumentModifiedInsideTransaction(key),
					LastModified = Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]).Value,
					Etag = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"]).TransfromToGuidWithProperSorting(),
					Metadata = metadata
				};
			} while (Api.TryMoveNext(session, Documents));
		}


		public IEnumerable<JsonDocument> GetDocumentsWithIdStartingWith(string idPrefix, int start)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, idPrefix, Encoding.Unicode, MakeKeyGrbit.NewKey);
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekGE) == false)
				yield break;
			do
			{
				Api.MakeKey(session, Documents, idPrefix, Encoding.Unicode, MakeKeyGrbit.NewKey | MakeKeyGrbit.SubStrLimit);
				if (Api.TrySetIndexRange(session, Documents, SetIndexRangeGrbit.RangeUpperLimit | SetIndexRangeGrbit.RangeInclusive) == false)
					yield break;

				while (start > 0)
				{
					if (Api.TryMoveNext(session, Documents) == false)
						yield break;
					start--;
				}

				var data = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]);
				var key = Api.RetrieveColumnAsString(session, Documents, tableColumnsCache.DocumentsColumns["key"], Encoding.Unicode);
				var metadata = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();

				data = documentCodecs.Aggregate(data, (bytes, codec) => codec.Decode(key, metadata, bytes));

				yield return new JsonDocument
				{
					Key = key,
					DataAsJson = data.ToJObject(),
					NonAuthoritiveInformation = IsDocumentModifiedInsideTransaction(key),
					LastModified = Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]).Value,
					Etag = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"]).TransfromToGuidWithProperSorting(),
					Metadata = metadata
				};
			} while (Api.TryMoveNext(session, Documents));
		}

		public IEnumerable<Tuple<JsonDocument, int>> DocumentsById(int startId, int endId)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_id");
			Api.MakeKey(session, Documents, startId, MakeKeyGrbit.NewKey);
			// this sholdn't really happen, it means that the doc is missing
			// probably deleted before we can get it?
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekGE) == false)
			{
				logger.DebugFormat("Document with id {0} or higher was not found", startId);
				yield break;
			}
			do
			{
				var id = Api.RetrieveColumnAsInt32(session, Documents, tableColumnsCache.DocumentsColumns["id"],
												   RetrieveColumnGrbit.RetrieveFromIndex).Value;
				if (id > endId)
					break;

				var data = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]);
				logger.DebugFormat("Document with id '{0}' was found, doc length: {1}", id, data.Length);
				var etag = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"]).TransfromToGuidWithProperSorting();
				var metadata = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();
				var key = Api.RetrieveColumnAsString(session, Documents, tableColumnsCache.DocumentsColumns["key"], Encoding.Unicode);
				var modified = Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]);
				data = documentCodecs.Aggregate(data, (bytes, codec) => codec.Decode(key, metadata, bytes));

				var doc = new JsonDocument
				{
					Key = key,
					DataAsJson = data.ToJObject(),
					NonAuthoritiveInformation = IsDocumentModifiedInsideTransaction(key),
					Etag = etag,
					LastModified = modified.Value,
					Metadata = metadata
				};
				yield return new Tuple<JsonDocument, int>(doc, id);
			} while (Api.TryMoveNext(session, Documents));
		}

		public Guid AddDocument(string key, Guid? etag, RavenJObject data, RavenJObject metadata)
		{
			if (key != null && Encoding.Unicode.GetByteCount(key) >= 255)
				throw new ArgumentException("The key must be a maximum of 255 bytes in unicode, 127 characters, key is: " + key, "key");

			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			var isUpdate = Api.TrySeek(session, Documents, SeekGrbit.SeekEQ);
			if (isUpdate)
			{
				EnsureNotLockedByTransaction(key, null);
				EnsureDocumentEtagMatch(key, etag, "PUT");
			}
			else
			{
				EnsureDocumentIsNotCreatedInAnotherTransaction(key, Guid.NewGuid());
				if (Api.TryMoveFirst(session, Details))
					Api.EscrowUpdate(session, Details, tableColumnsCache.DetailsColumns["document_count"], 1);
			}
			Guid newEtag = uuidGenerator.CreateSequentialUuid();

			var bytes = documentCodecs.Aggregate(data.ToBytes(), (current, codec) => codec.Encode(key, data, metadata, current));

			using (var update = new Update(session, Documents, isUpdate ? JET_prep.Replace : JET_prep.Insert))
			{
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["key"], key, Encoding.Unicode);
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"], bytes);
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["etag"], newEtag.TransformToValueForEsentSorting());
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"], DateTime.UtcNow);
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"], metadata.ToBytes());

				update.Save();
			}

			logger.DebugFormat("Inserted a new document with key '{0}', update: {1}, ",
							   key, isUpdate);

			return newEtag;
		}


		public Guid AddDocumentInTransaction(string key, Guid? etag, RavenJObject data, RavenJObject metadata, TransactionInformation transactionInformation)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			var isUpdate = Api.TrySeek(session, Documents, SeekGrbit.SeekEQ);
			if (isUpdate)
			{
				EnsureNotLockedByTransaction(key, transactionInformation.Id);
				EnsureDocumentEtagMatchInTransaction(key, etag);
				using (var update = new Update(session, Documents, JET_prep.Replace))
				{
					Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["locked_by_transaction"], transactionInformation.Id.ToByteArray());
					update.Save();
				}
			}
			else
			{
				EnsureDocumentIsNotCreatedInAnotherTransaction(key, transactionInformation.Id);
			}
			EnsureTransactionExists(transactionInformation);
			Guid newEtag = uuidGenerator.CreateSequentialUuid();

			Api.JetSetCurrentIndex(session, DocumentsModifiedByTransactions, "by_key");
			Api.MakeKey(session, DocumentsModifiedByTransactions, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			var isUpdateInTransaction = Api.TrySeek(session, DocumentsModifiedByTransactions, SeekGrbit.SeekEQ);

			var bytes = documentCodecs.Aggregate(data.ToBytes(), (current, codec) => codec.Encode(key, data, metadata, current));
			using (var update = new Update(session, DocumentsModifiedByTransactions, isUpdateInTransaction ? JET_prep.Replace : JET_prep.Insert))
			{
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["key"], key, Encoding.Unicode);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["data"], bytes);
				Api.SetColumn(session, DocumentsModifiedByTransactions,
							  tableColumnsCache.DocumentsModifiedByTransactionsColumns["etag"],
							  newEtag.TransformToValueForEsentSorting());
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["metadata"], metadata.ToBytes());
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["last_modified"], DateTime.UtcNow);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["delete_document"], false);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["locked_by_transaction"], transactionInformation.Id.ToByteArray());

				update.Save();
			}
			logger.DebugFormat("Inserted a new document with key '{0}', update: {1}, in transaction: {2}",
							   key, isUpdate, transactionInformation.Id);

			return newEtag;
		}


		public bool DeleteDocument(string key, Guid? etag, out RavenJObject metadata)
		{
			metadata = null;
			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekEQ) == false)
			{
				logger.DebugFormat("Document with key '{0}' was not found, and considered deleted", key);
				return false;
			}
			if (Api.TryMoveFirst(session, Details))
				Api.EscrowUpdate(session, Details, tableColumnsCache.DetailsColumns["document_count"], -1);

			EnsureDocumentEtagMatch(key, etag, "DELETE");
			EnsureNotLockedByTransaction(key, null);

			metadata = Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]).ToJObject();

			Api.JetDelete(session, Documents);
			logger.DebugFormat("Document with key '{0}' was deleted", key);
			return true;
		}


		public void DeleteDocumentInTransaction(TransactionInformation transactionInformation, string key, Guid? etag)
		{
			Api.JetSetCurrentIndex(session, Documents, "by_key");
			Api.MakeKey(session, Documents, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			if (Api.TrySeek(session, Documents, SeekGrbit.SeekEQ) == false)
			{
				logger.DebugFormat("Document with key '{0}' was not found, and considered deleted", key);
				return;
			}

			EnsureNotLockedByTransaction(key, transactionInformation.Id);
			EnsureDocumentEtagMatchInTransaction(key, etag);

			using (var update = new Update(session, Documents, JET_prep.Replace))
			{
				Api.SetColumn(session, Documents, tableColumnsCache.DocumentsColumns["locked_by_transaction"], transactionInformation.Id.ToByteArray());
				update.Save();
			}
			EnsureTransactionExists(transactionInformation);

			Guid newEtag = uuidGenerator.CreateSequentialUuid();

			Api.JetSetCurrentIndex(session, DocumentsModifiedByTransactions, "by_key");
			Api.MakeKey(session, DocumentsModifiedByTransactions, key, Encoding.Unicode, MakeKeyGrbit.NewKey);
			var isUpdateInTransaction = Api.TrySeek(session, DocumentsModifiedByTransactions, SeekGrbit.SeekEQ);

			using (var update = new Update(session, DocumentsModifiedByTransactions, isUpdateInTransaction ? JET_prep.Replace : JET_prep.Insert))
			{
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["key"], key, Encoding.Unicode);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["data"],
					Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["data"]));
				Api.SetColumn(session, DocumentsModifiedByTransactions,
							  tableColumnsCache.DocumentsModifiedByTransactionsColumns["etag"],
							  newEtag.TransformToValueForEsentSorting());
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["last_modified"],
					Api.RetrieveColumnAsDateTime(session, Documents, tableColumnsCache.DocumentsColumns["last_modified"]).Value);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["metadata"],
					Api.RetrieveColumn(session, Documents, tableColumnsCache.DocumentsColumns["metadata"]));
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["delete_document"], true);
				Api.SetColumn(session, DocumentsModifiedByTransactions, tableColumnsCache.DocumentsModifiedByTransactionsColumns["locked_by_transaction"], transactionInformation.Id.ToByteArray());

				update.Save();
			}
		}


		public Tuple<int, int> FirstAndLastDocumentIds()
		{
			var item1 = 0;
			var item2 = 0;
			Api.JetSetCurrentIndex(session, Documents, "by_id");
			Api.MoveBeforeFirst(session, Documents);
			if (Api.TryMoveNext(session, Documents))
				item1 = Api.RetrieveColumnAsInt32(session, Documents, tableColumnsCache.DocumentsColumns["id"]).Value;
			Api.MoveAfterLast(session, Documents);
			if (Api.TryMovePrevious(session, Documents))
				item2 = Api.RetrieveColumnAsInt32(session, Documents, tableColumnsCache.DocumentsColumns["id"]).Value;
			return new Tuple<int, int>(item1, item2);
		}
	}
}
