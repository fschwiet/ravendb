using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexes;
using Xunit;

namespace Raven.Tests.Bugs.MultiMap
{
	public class GroupedByAlternateKeyMultimapReduce : MultiMapReduce
	{
		public class Post : MultiMapReduce.Post
		{
			public string PostersName;
		}

		public class LastLetterPostingStats
		{
			public string[] UserNames { get; set; }
			public int PostCount { get; set; }
			public string FirstLetterOfName { get; set; }
		}

		public class PostCountsByFirstLetter_WithNames : AbstractMultiMapIndexCreationTask<LastLetterPostingStats>
		{
			public PostCountsByFirstLetter_WithNames()
			{
				AddMap<User>(users => from user in users
									  select new
									  {
										  UserNames = new string[] {user.Name},
										  PostCount = 0,
										  FirstLetterOfName = string.IsNullOrEmpty(user.Name) ? " " : user.Name.Substring(0, 1)
									  });

				AddMap<Post>(posts => from post in posts
									  select new
									  {
										  UserNames = new string[0],
										  PostCount = 1,
										  FirstLetterOfName = string.IsNullOrEmpty(post.PostersName) ? " " : post.PostersName.Substring(0, 1)
									  });

				Reduce = results => from result in results
									group result by result.FirstLetterOfName
										into g
										select new
										{
											UserNames = g.SelectMany(r => r.UserNames).Distinct().ToArray(),
											PostCount = g.Sum(x => x.PostCount),
											FirstLetterOfName = g.Key
										};

				Index(x => x.FirstLetterOfName, FieldIndexing.Analyzed);
			}
		}


		[Fact]
		public void CanGroupByFirstLetterOfName()
		{
			using (var store = NewDocumentStore())
			{
				new PostCountsByFirstLetter_WithNames().Execute(store);

				using (var session = store.OpenSession())
				{
					var userAyende = new User
					{
						Name = "Ayende Rahien"
					};

					session.Store(userAyende);

					session.Store(new User()
					{
						Name = "Another User"
					});

					session.Store(new User()
					{
						Name = "Billionth"
					});

					for (int i = 0; i < 5; i++)
					{
						session.Store(new Post
						{
							AuthorId = userAyende.Id,
							Title = "blah",
							PostersName = userAyende.Name
						});
					}

					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					var ups = session.Query<LastLetterPostingStats, PostCountsByFirstLetter_WithNames>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.FirstLetterOfName == "a")
						.Single();

					Assert.True(ups.FirstLetterOfName == "a");
					Assert.True(ups.UserNames.Contains("Ayende Rahien"));
					Assert.True(ups.UserNames.Contains("Another User"));
					Assert.True(!ups.UserNames.Contains("Another User"));
					Assert.Equal(5, ups.PostCount);
				}
			}
		}
	}
}
