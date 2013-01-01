using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexes;
using Xunit;
using Xunit.Extensions;

namespace Raven.Tests.Indexes
{
    public class MultipleSortFields : RavenTest
    {
        public class Item
        {
            public string Name;
            public int OriginalOrder;
            public int SubOriginalOrder;
        }

        public class ItemIndex : AbstractIndexCreationTask<Item>
        {
            public ItemIndex()
            {
                Map = items => from item in items select new {item.OriginalOrder, item.SubOriginalOrder};
            }
        }

        [Fact]
        public void DefaultBehaviorIsToGiveEarlierFieldsPrecedence()
        {
            using (var store = NewDocumentStore())
            {
                new ItemIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Item() { Name = "a", OriginalOrder = 1 });
                    session.Store(new Item() { Name = "b", OriginalOrder = 2, SubOriginalOrder = 1});
                    session.Store(new Item() { Name = "bb", OriginalOrder = 2, SubOriginalOrder = 2 });
                    session.Store(new Item() { Name = "c", OriginalOrder = 3 });
                    session.SaveChanges();
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var originalOrder = session.Advanced.LuceneQuery<Item,ItemIndex>()
                                               .OrderBy("OriginalOrder", "SubOriginalOrder")
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new [] {"a", "b", "bb", "c"}, originalOrder);

                    var tweakedOrder = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalOrder", "-SubOriginalOrder")
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "a", "bb", "b", "c" }, tweakedOrder);
                }
            }
        }

        [Fact]
        public void CanSortByMaximumValueOfAllSortFields()
        {
            using (var store = NewDocumentStore())
            {
                new ItemIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Item() { Name = "a", OriginalOrder = 5, SubOriginalOrder = 1 });
                    session.Store(new Item() { Name = "b", OriginalOrder = 1, SubOriginalOrder = 4 });
                    session.Store(new Item() { Name = "c", OriginalOrder = 3, SubOriginalOrder = 1 });
                    session.Store(new Item() { Name = "d", OriginalOrder = 1, SubOriginalOrder = 2 });
                    session.Store(new Item() { Name = "e", OriginalOrder = 1, SubOriginalOrder = 1 });
                    session.SaveChanges();
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var originalOrder = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalOrder", "SubOriginalOrder")
                                               .UseSortFieldsWith(SortFieldAggregation.UseMaximum)
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "e", "d", "c", "b", "a"}, originalOrder);
                }
            }
        }
    }
}
