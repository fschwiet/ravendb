using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Search;
using Lucene.Net.Search.Function;
using Raven.Abstractions.Indexing;

namespace Raven.Database.Indexing.Sorting
{
    public class AggregateFieldsScoreQuery : CustomScoreQuery 
    {
        private readonly string[] fields;
        private readonly SortFieldAggregation strategy;

        public AggregateFieldsScoreQuery(Query subQuery, string[] fields, SortFieldAggregation strategy) : base(subQuery)
        {
            this.fields = fields;
            this.strategy = strategy;
        }

        protected override CustomScoreProvider GetCustomScoreProvider(Lucene.Net.Index.IndexReader reader)
        {
            return new AggregateFieldsScoreProvider(reader, fields, strategy);
        }
    }
}
