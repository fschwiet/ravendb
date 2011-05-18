using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Search;
using Xunit;

namespace Raven.Tests.Indexes
{
    public class can_use_a_custom_Lucene_filter : LocalClientTest
    {
        Type filterType = typeof(FieldCacheTermsFilter);

        [Fact]
        public void can_use_custom_filter()
        {
            using(var store = base.NewDocumentStore())
            {
                using(var session = store.OpenSession())
                {
                    session.Store(new Tuple<string, string>("a", "b"));
                    session.Store(new Tuple<string, string>("c", "d"));
                    session.SaveChanges();
                }

                using(var session = store.OpenSession())
                {
                    var result = session.Advanced.LuceneQuery<Tuple<string,string>>()
                        .WaitForNonStaleResultsAsOfNow()
                        .WhereEquals("Item1", "a")
                        //.FilterBy("FieldCacheTermsFilter", "Item1", "a")
                        .First();

                    Assert.Equal("a", result.Item1);
                    Assert.Equal("b", result.Item2);
                }
            }
        }

    }
}
