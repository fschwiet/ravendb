using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Function;
using Microsoft.Isam.Esent.Interop;
using Raven.Abstractions.Indexing;

namespace Raven.Database.Indexing.Sorting
{
    public class AggregateFieldsScoreProvider : CustomScoreProvider
    {
        private readonly IndexReader reader;
        private readonly string[] fields;
        private readonly SortFieldAggregation fieldAggregation;

        public AggregateFieldsScoreProvider(IndexReader reader, string[] fields, SortFieldAggregation fieldAggregation) : base(reader)
        {
            this.reader = reader;
            this.fields = fields;
            this.fieldAggregation = fieldAggregation;

            if (fieldAggregation != SortFieldAggregation.UseMaximum &&
                fieldAggregation != SortFieldAggregation.UseMinimum)
            {
                throw new ArgumentException("Value must be UseMaximum or UseMinimum, was: " + fieldAggregation, "fieldAggregation");
            }
        }

        public override Explanation CustomExplain(int doc, Lucene.Net.Search.Explanation subQueryExpl, Lucene.Net.Search.Explanation valSrcExpl)
        {
            return CustomExplain(doc, subQueryExpl, new [] {valSrcExpl});
        }

        public override Explanation CustomExplain(int doc, Lucene.Net.Search.Explanation subQueryExpl, Lucene.Net.Search.Explanation[] valSrcExpls)
        {
            // untested
            return new Explanation(CustomScore(doc, 0, 0), String.Format("Aggregating fields {0} with {1}.",
                string.Join(", ", fields), fieldAggregation));
        }

        public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
        {
            return CustomScore(doc, subQueryScore, new [] {valSrcScore});
        }

        public override float CustomScore(int doc, float subQueryScore, float[] valSrcScores)
        {
            var document = reader[doc];
            var values = fields.SelectMany(f => document.GetValues(f)).Select(v => long.Parse(v));

            if (fieldAggregation == SortFieldAggregation.UseMaximum)
                return values.Max();
            else
                return values.Min();
        }
    }
}
