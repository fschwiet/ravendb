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
            public DateTime OriginalDate;
            public DateTime SubOriginalDate;
        }

        public class ItemIndex : AbstractIndexCreationTask<Item>
        {
            public ItemIndex()
            {
                Map = items => from item in items
                               select new
                               {
                                   item.OriginalOrder,
                                   item.SubOriginalOrder,
                                   OriginalDate = Math.Floor(TimeSpan.FromTicks(item.OriginalDate.Ticks - new DateTime(1990, 1, 1).Ticks).TotalSeconds),
                                   SubOriginalDate = Math.Floor(TimeSpan.FromTicks(item.SubOriginalDate.Ticks - new DateTime(1990, 1, 1).Ticks).TotalSeconds)
                               };

                //  Store is required for SortFieldAggregation.UseMinimum/SortFieldAggregation.UseMaximum
                //  but not SortFieldAggregation.InOrder.  Can this be improved?  (give error warnings when not stored, or use Sort() instead, etc)
                Store(i => i.OriginalOrder, FieldStorage.Yes);
                Store(i => i.SubOriginalOrder, FieldStorage.Yes);

                //  I'd like a way to use DAteTime values natively here, but since CustomScoreProvider uses float precision
                //  and people may be working with different precisions / date ranges, then no good solution exists.

                //  I was hoping for precision including seconds, but only was able to get minute precision so far.
                Store(i => i.OriginalDate, FieldStorage.Yes);
                Store(i => i.SubOriginalDate, FieldStorage.Yes);
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
                    session.Store(new Item() { Name = "a", OriginalOrder = -5, SubOriginalOrder = 5 });
                    session.Store(new Item() { Name = "b", OriginalOrder = 4, SubOriginalOrder = -4 });
                    session.Store(new Item() { Name = "c", OriginalOrder = -3, SubOriginalOrder = 3 });
                    session.Store(new Item() { Name = "d", OriginalOrder = 2, SubOriginalOrder = -2 });
                    session.Store(new Item() { Name = "e", OriginalOrder = -1, SubOriginalOrder = 1 });
                    session.SaveChanges();
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var results = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalOrder", "SubOriginalOrder")
                                               .UseSortFieldsWith(SortFieldAggregation.UseMinimum)
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "e", "d", "c", "b", "a" }, results);
                }

                using (var session = store.OpenSession())
                {
                    var results = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalOrder", "SubOriginalOrder")
                                               .UseSortFieldsWith(SortFieldAggregation.UseMaximum)
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "a", "b", "c", "d", "e" }, results);
                }
            }
        }

        [Fact]
        public void CanUseMinMaxOfDateFields()
        {
            using (var store = NewDocumentStore())
            {
                new ItemIndex().Execute(store);

                var now = DateTime.UtcNow;

                using (var session = store.OpenSession())
                {
                    session.Store(new Item() { Name = "a", OriginalDate = now.AddMinutes(-5), SubOriginalDate = now.AddMinutes(5) });
                    session.Store(new Item() { Name = "b", OriginalDate = now.AddMinutes(4), SubOriginalDate = now.AddMinutes(-4) });
                    session.Store(new Item() { Name = "c", OriginalDate = now.AddMinutes(-3), SubOriginalDate = now.AddMinutes(3) });
                    session.Store(new Item() { Name = "d", OriginalDate = now.AddMinutes(2), SubOriginalDate = now.AddMinutes(-2) });
                    session.Store(new Item() { Name = "e", OriginalDate = now.AddMinutes(-1), SubOriginalDate = now.AddMinutes(1) });
                    session.SaveChanges();
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var results = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalDate", "SubOriginalDate")
                                               .UseSortFieldsWith(SortFieldAggregation.UseMinimum)
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "e", "d", "c", "b", "a" }, results);
                }

                using (var session = store.OpenSession())
                {
                    var results = session.Advanced.LuceneQuery<Item, ItemIndex>()
                                               .OrderBy("OriginalDate", "SubOriginalDate")
                                               .UseSortFieldsWith(SortFieldAggregation.UseMaximum)
                                               .Select(d => d.Name).ToArray();

                    Assert.Equal(new[] { "a", "b", "c", "d", "e" }, results);
                }
            }
        }
    }
}
