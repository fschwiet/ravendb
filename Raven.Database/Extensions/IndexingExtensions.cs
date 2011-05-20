//-----------------------------------------------------------------------
// <copyright file="IndexingExtensions.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Database.Data;
using Raven.Database.Indexing;
using Constants = Raven.Abstractions.Data.Constants;

namespace Raven.Database.Extensions
{
    public static class IndexingExtensions
    {
        public static Analyzer CreateAnalyzerInstance(string name, string analyzerTypeAsString)
        {
            var analyzerType = typeof(StandardAnalyzer).Assembly.GetType(analyzerTypeAsString) ??
                Type.GetType(analyzerTypeAsString);
            if (analyzerType == null)
                throw new InvalidOperationException("Cannot find analzyer type '" + analyzerTypeAsString + "' for field: " + name);
            try
            {
                return (Analyzer)Activator.CreateInstance(analyzerType);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "Could not create new analyzer instance '" + name + "' for field: " +
                        name, e);
            }
        }

        public static Analyzer GetAnalyzer(this IndexDefinition self, string name)
        {
            if (self.Analyzers == null)
                return null;
            string analyzerTypeAsString;
            if (self.Analyzers.TryGetValue(name, out analyzerTypeAsString) == false)
                return null;
            return CreateAnalyzerInstance(name, analyzerTypeAsString);
        }

        public static Filter CreateFilterInstance(string filterTypeAsString, object[] constructorParameters)
        {
            var filterType = typeof(StandardFilter).Assembly.GetType(filterTypeAsString) ??
                Type.GetType(filterTypeAsString);
            if (filterType == null)
                throw new InvalidOperationException("Cannot find filter type '" + filterTypeAsString + "'.");
            try
            {
                return (Filter)Activator.CreateInstance(filterType, constructorParameters);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "Could not create new filter instance '" + filterTypeAsString + "', exception: " + e.Message, e);
            }
        }

        public static Filter GetFilter(this IndexQuery self)
        {
            //  should handle case where multiple filters are defined..
            //  why isn't Lucene.Net.Search.BooleanFilter available?

            if (self.FilterType != null)
            {
                return CreateFilterInstance(self.FilterType, self.FilterConstructorParameters ?? new object[0]);
            }

            var spatialIndexQuery = self as SpatialIndexQuery;
            if(spatialIndexQuery != null)
            {
                var dq = new Lucene.Net.Spatial.Tier.DistanceQueryBuilder(
                    spatialIndexQuery.Latitude,
                    spatialIndexQuery.Longitude,
                    spatialIndexQuery.Radius,
                    SpatialIndex.LatField,
                    SpatialIndex.LngField,
                    Lucene.Net.Spatial.Tier.Projectors.CartesianTierPlotter.DefaltFieldPrefix,
                    true);

                return dq.Filter;
            }
            return null;
        }

        public static Field.Index GetIndex(this IndexDefinition self, string name, Field.Index defaultIndex)
        {
            if (self.Indexes == null)
                return defaultIndex;
            FieldIndexing value;
            if (self.Indexes.TryGetValue(name, out value) == false)
            {
                string ignored;
                if (self.Analyzers.TryGetValue(name, out ignored))
                    return Field.Index.ANALYZED;// if there is a custom analyzer, the value should be analyzed
                return defaultIndex;
            }
            switch (value)
            {
                case FieldIndexing.No:
                    return Field.Index.NO;
                case FieldIndexing.Analyzed:
                    return Field.Index.ANALYZED_NO_NORMS;
                case FieldIndexing.NotAnalyzed:
                    return Field.Index.NOT_ANALYZED_NO_NORMS;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static Field.Store GetStorage(this IndexDefinition self, string name, Field.Store defaultStorage)
        {
            if (self.Stores == null)
                return defaultStorage;
            FieldStorage value;
            if (self.Stores.TryGetValue(name, out value) == false)
                return defaultStorage;
            switch (value)
            {
                case FieldStorage.Yes:
                    return Field.Store.YES;
                case FieldStorage.No:
                    return Field.Store.NO;
                case FieldStorage.Compress:
                    return Field.Store.COMPRESS;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static Sort GetSort(this IndexQuery self, Filter filter, IndexDefinition indexDefinition)
        {
        	if (self.SortedFields == null || self.SortedFields.Length <= 0)
        		return null;
        	var isSpatialIndexQuery = self is SpatialIndexQuery;
			return new Sort(self.SortedFields
							.Select(sortedField =>
							{
								if (isSpatialIndexQuery && sortedField.Field == Constants.DistanceFieldName)
								{
									var dsort = new Lucene.Net.Spatial.Tier.DistanceFieldComparatorSource((Lucene.Net.Spatial.Tier.DistanceFilter)filter);
									return new SortField(Constants.DistanceFieldName, dsort, sortedField.Descending);
								}
								var sortOptions = GetSortOption(indexDefinition, sortedField.Field);
								if (sortOptions == null)
									return new SortField(sortedField.Field, CultureInfo.InvariantCulture, sortedField.Descending);
								return new SortField(sortedField.Field, (int)sortOptions.Value, sortedField.Descending);
							})
							.ToArray());
        }

        public static SortOptions? GetSortOption(this IndexDefinition self, string name)
        {
            SortOptions value;
            if (!self.SortOptions.TryGetValue(name, out value))
            {
                if (!name.EndsWith("_Range"))
                {
                    return null;
                }
                string nameWithoutRange = name.Substring(0, name.Length - "_Range".Length);
                if (!self.SortOptions.TryGetValue(nameWithoutRange, out value))
                    return null;
            }
            return value;
        }
    }
}