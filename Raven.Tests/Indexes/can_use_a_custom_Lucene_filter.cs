﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Search;
using Raven.Client.Document;
using Xunit;

namespace Raven.Tests.Indexes
{
    public class can_use_a_custom_Lucene_filter : RemoteClientTest
    {
        [Fact]
        public void can_use_custom_filter()
        {
            using (GetNewServer())
            using (var store = new DocumentStore { Url = "http://localhost:8080" })
            {
                store.Initialize();

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
                        .Where("Item1:*")
                        .FilterBy(typeof(FieldCacheTermsFilter).FullName, "Item1", new string[]{"a"})
                        .Single();

                    Assert.Equal("a", result.Item1);
                    Assert.Equal("b", result.Item2);
                }
            }
        }
    }
}
