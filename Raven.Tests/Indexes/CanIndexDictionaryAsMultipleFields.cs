using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexes;
using Xunit;

namespace Raven.Tests.Indexes
{
    public class CanIndexDictionaryAsMultipleFields : RavenTest
    {
        public class Item
        {
            public Dictionary<int, DateTime> Values = new Dictionary<int, DateTime>();
        }

        public class ItemIndex : AbstractIndexCreationTask<Item>
        {
            public ItemIndex()
            {
                Map = items => from item in items select new
                {
                    _ = item.Values.Select(kvp => CreateField("Values" + kvp.Key, kvp.Value, true, true))
                };
            }
        }

        [Fact]
        public void CanCreate()
        {
            using (var store = NewDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item() { Values = {{1, DateTime.Now}, {2, DateTime.Now.AddDays(1)}}});
                    session.Store(new Item() { Values = {{1, DateTime.Now}, {3, DateTime.Now.AddDays(-1)}}});
                    session.SaveChanges();
                }

                new ItemIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    var v1 =
                        session.Advanced.LuceneQuery<Item, ItemIndex>()
                               .WaitForNonStaleResultsAsOfNow()
                               .Where("Values1:*")
                               .ToArray();

                    Assert.Equal(2, v1.Count());

                    var v2 =
                        session.Advanced.LuceneQuery<Item, ItemIndex>()
                               .WaitForNonStaleResultsAsOfNow()
                               .Where(" Values2:*")
                               .OrderBy()
                               .ToArray();

                    Assert.Equal(1, v2.Count());

                    var v3 =
                        session.Advanced.LuceneQuery<Item, ItemIndex>()
                               .WaitForNonStaleResultsAsOfNow()
                               .Where("Values3:*")
                               .ToArray();

                    Assert.Equal(2, v3.Count());
                }
            }
        }
    }
}
