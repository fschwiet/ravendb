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
            public int Id;
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
                    session.Store(new Item() { Id = 1, Values = {{1, DateTime.Now}}});
                    session.Store(new Item() { Id = 2, Values = { { 1, DateTime.Now.AddHours(1) }} });
                    session.Store(new Item() { Id = 3, Values = { { 1, DateTime.Now.AddHours(-1) } } });
                    session.SaveChanges();
                }

                new ItemIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    var v1 =
                        session.Advanced.LuceneQuery<Item, ItemIndex>()
                               .WaitForNonStaleResultsAsOfNow()
                               .Where("Values1:*").OrderBy()
                               .ToArray();

                    Assert.Equal(3, v1.Count());
                    Assert.Equal(new [] {3,1,2}, v1.Select(d => d.Id));
                }
            }
        }
    }
}
