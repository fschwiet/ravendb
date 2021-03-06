﻿using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Linq;
using Raven.Client.Util;
using Raven.Imports.Newtonsoft.Json;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Imports.Newtonsoft.Json.Serialization;
using Raven.Json.Linq;

namespace Raven.Client.Document
{
	public class EntityToJson
	{
		private readonly IDocumentStore documentStore;

		/// <summary>
		/// All the listeners for this session
		/// </summary>
		public DocumentSessionListeners Listeners { get; private set; }

		public EntityToJson(IDocumentStore documentStore, DocumentSessionListeners listeners)
		{
			this.documentStore = documentStore;
			Listeners = listeners;
		}

		public RavenJObject ConvertEntityToJson(string key, object entity, RavenJObject metadata)
		{
			var entityType = entity.GetType();
			var identityProperty = documentStore.Conventions.GetIdentityProperty(entityType);

			var objectAsJson = GetObjectAsJson(entity);
			if (identityProperty != null)
			{
				objectAsJson.Remove(identityProperty.Name);
			}

			SetClrType(entityType, metadata);

			foreach (var documentConversionListener in Listeners.ConversionListeners)
			{
				documentConversionListener.EntityToDocument(key, entity, objectAsJson, metadata);
			}

			return objectAsJson;
		}

		public IDictionary<object, RavenJObject> CachedJsonDocs { get; private set; }

		private RavenJObject GetObjectAsJson(object entity)
		{
			var jObject = entity as RavenJObject;
			if (jObject != null)
				return jObject;

			if (CachedJsonDocs != null && CachedJsonDocs.TryGetValue(entity, out jObject))
				return (RavenJObject)jObject.CreateSnapshot();

			var jsonSerializer = documentStore.Conventions.CreateSerializer();
			jObject = RavenJObject.FromObject(entity, jsonSerializer);
			if (jsonSerializer.TypeNameHandling == TypeNameHandling.Auto)// remove the default types
			{
				var resolveContract = jsonSerializer.ContractResolver.ResolveContract(entity.GetType());
				TrySimplfyingJson(jObject, resolveContract);
			}

			if (CachedJsonDocs != null)
			{
				jObject.EnsureSnapshot();
				CachedJsonDocs[entity] = jObject;
				return (RavenJObject)jObject.CreateSnapshot();
			}
			return jObject;
		}

		private void SetClrType(Type entityType, RavenJObject metadata)
		{
			if (
				entityType == typeof(DynamicJsonObject) ||
				entityType == typeof(RavenJObject)) // dynamic types
			{
				return;// do not overwrite the value
			}

			metadata[Constants.RavenClrType] = documentStore.Conventions.GetClrTypeName(entityType);
		}


		/// <summary>
		/// All calls to convert an entity to a json object would be cache
		/// This is used inside the SaveChanges() action, where we need to access the entities json
		/// in several disparate places.
		/// 
		/// Note: This assumes that no modifications can happen during the SaveChanges. This is naturally true
		/// Note: for SaveChanges (and multi threaded access will cause undefined behavior anyway).
		/// Note: For SaveChangesAsync, the same holds true as well.
		/// </summary>
		public IDisposable EntitiesToJsonCachingScope()
		{
			CachedJsonDocs = new Dictionary<object, RavenJObject>(ObjectReferenceEqualityComparerer<object>.Default);
			return new DisposableAction(() => CachedJsonDocs = null);
		}

		private static void TrySimplfyingJson(RavenJObject jObject, JsonContract contract)
		{
			var objectContract = contract as JsonObjectContract;
			if (objectContract == null)
				return;

			var deferredActions = new List<Action>();
			foreach (var kvp in jObject)
			{
				var prop = kvp;
				if (prop.Value == null)
					continue;
				var obj = prop.Value as RavenJObject;
				if (obj == null)
					continue;

				var jsonProperty = objectContract.Properties.GetClosestMatchProperty(prop.Key);

				if (ShouldSimplfyJsonBasedOnType(obj.Value<string>("$type"), jsonProperty) == false)
					continue;

				if (obj.ContainsKey("$values") == false)
				{
					deferredActions.Add(() => obj.Remove("$type"));
				}
				else
				{
					deferredActions.Add(() => jObject[prop.Key] = obj["$values"]);
				}
			}
			foreach (var deferredAction in deferredActions)
			{
				deferredAction();
			}

			foreach (var prop in jObject.Where(prop => prop.Value != null))
			{
				switch (prop.Value.Type)
				{
					case JTokenType.Array:
						foreach (var item in ((RavenJArray)prop.Value))
						{
							var ravenJObject = item as RavenJObject;
							if (ravenJObject != null)
								TrySimplfyingJson(ravenJObject, contract);
						}
						break;
					case JTokenType.Object:
						TrySimplfyingJson((RavenJObject)prop.Value, contract);
						break;
				}
			}
		}

		private static bool ShouldSimplfyJsonBasedOnType(string typeValue, JsonProperty jsonProperty)
		{
			if (jsonProperty != null && (jsonProperty.TypeNameHandling == TypeNameHandling.All || jsonProperty.TypeNameHandling == TypeNameHandling.Arrays))
				return false; // explicitly rejected what we are trying to do here

			if (typeValue == null)
				return false;
			if (typeValue.StartsWith("System.Collections.Generic.List`1[["))
				return true;
			if (typeValue.StartsWith("System.Collections.Generic.Dictionary`2[["))
				return true;
			if (typeValue.EndsWith("[], mscorlib")) // array
				return true;
			return false;
		}
	}
}