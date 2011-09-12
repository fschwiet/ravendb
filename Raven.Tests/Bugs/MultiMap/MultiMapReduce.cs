using System.Linq;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexes;
using Xunit;

namespace Raven.Tests.Bugs.MultiMap
{
	public class MultiMapReduce : LocalClientTest
	{
		[Fact]
		public void CanGetDataFromMultipleDocumentSources()
		{
			using(var store = NewDocumentStore())
			{
				new PostCountsByUser_WithName().Execute(store);

				using(var session = store.OpenSession())
				{
					var user = new User
					{
						Name = "Ayende Rahien"
					};
					session.Store(user);

					for (int i = 0; i < 5; i++)
					{
						session.Store(new Post
						{
							AuthorId = user.Id,
							Title = "blah"
						});
					}

					session.SaveChanges();
				}

				using(var session = store.OpenSession())
				{
					var ups = session.Query<UserPostingStats, PostCountsByUser_WithName>()
						.Customize(x=>x.WaitForNonStaleResults())
						.ToList();

					Assert.Equal(1, ups.Count);

					Assert.Equal(5, ups[0].PostCount);
					Assert.Equal("Ayende Rahien", ups[0].UserName);
				}
			}

		}

		[Fact]
		public void CanQueryFromMultipleSources()
		{
			using (var store = NewDocumentStore())
			{
				new PostCountsByUser_WithName().Execute(store);

				using (var session = store.OpenSession())
				{
					var user = new User
					{
						Name = "Ayende Rahien"
					};
					session.Store(user);

					for (int i = 0; i < 5; i++)
					{
						session.Store(new Post
						{
							AuthorId = user.Id,
							Title = "blah"
						});
					}

					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					var ups = session.Query<UserPostingStats, PostCountsByUser_WithName>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x=>x.UserName.StartsWith("aye"))
						.ToList();

					Assert.Equal(1, ups.Count);

					Assert.Equal(5, ups[0].PostCount);
					Assert.Equal("Ayende Rahien", ups[0].UserName);
				}
			}

		}

		[Fact]
		public void CanQueryFromMultipleSources2()
		{
			using (var store = NewDocumentStore())
			{
				new PostCountsByUser_WithName().Execute(store);

				using (var session = store.OpenSession())
				{
					var user = new User
					{
						Name = "Ayende Rahien"
					};
					session.Store(user);

					for (int i = 0; i < 5; i++)
					{
						session.Store(new Post
						{
							AuthorId = user.Id,
							Title = "blah"
						});
					}

					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					var ups = session.Query<UserPostingStats, PostCountsByUser_WithName>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.UserName.StartsWith("rah"))
						.ToList();

					Assert.Equal(1, ups.Count);

					Assert.Equal(5, ups[0].PostCount);
					Assert.Equal("Ayende Rahien", ups[0].UserName);
				}
			}
		}

		[Fact]
		public void CanQueryByFirstLetterOfLastName()
		{
			using (var store = NewDocumentStore())
			{
				new PostCountsByUser_WithName().Execute(store);

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
							Title = "blah"
						});
					}

					session.SaveChanges();
				}

				using (var session = store.OpenSession())
				{
					var ups = session.Query<UserPostingStats, PostCountsByUser_WithName>()
						.Customize(x => x.WaitForNonStaleResults())
						.Where(x => x.FirstLetterOfLastName == "a")
						.ToList();

					var firstletters = session.Advanced.DatabaseCommands.GetTerms(new PostCountsByUser_WithName().IndexName, "FirstLetterOfLastName", null, 1000).ToArray();
					var names = session.Advanced.DatabaseCommands.GetTerms(new PostCountsByUser_WithName().IndexName, "UserName", null, 1000).ToArray();

					Assert.Contains("b", firstletters);
					Assert.Contains("a", firstletters);  // fails here

					Assert.True(ups.All(u => u.FirstLetterOfLastName == "a"));
					Assert.True(ups.Any(u => u.UserName == "Ayende Rahien"));
					Assert.True(ups.Any(u => u.UserName == "Another User"));
				}
			}
		}


		public class User
		{
			public string Id { get; set; }
			public string Name { get; set; }
		}

		public class Post
		{
			public string Id { get; set; }
			public string Title { get; set; }
			public string AuthorId { get; set; }
		}

		public class UserPostingStats
		{
			public string UserName { get; set; }
			public string UserId { get; set; }
			public int PostCount { get; set; }
			public string FirstLetterOfLastName { get; set; }
		}

		public class PostCountsByUser_WithName : AbstractMultiMapIndexCreationTask<UserPostingStats>
		{
			public PostCountsByUser_WithName()
			{
				AddMap<User>(users => from user in users
									  select new
									  {
										  UserId = user.Id,
										  UserName = user.Name,
										  PostCount = 0,
										  FirstLetterOfLastName = string.IsNullOrEmpty(user.Name) ? " " : user.Name.Substring(0,1)
									  });

				AddMap<Post>(posts => from post in posts
									  select new
									  {
										  UserId = post.AuthorId,
										  UserName = (string)null,
										  PostCount = 1,
										  FirstLetterOfLastName = (string)null
									  });

				Reduce = results => from result in results
									group result by result.UserId
									into g
									select new
									{
										UserId = g.Key,
										UserName = g.Select(x => x.UserName).Where(x => x != null).FirstOrDefault(),
										PostCount = g.Sum(x => x.PostCount),
										FirstLetterOfLastName = g.Select(x => x.FirstLetterOfLastName).Where(x => x != null).FirstOrDefault()
									};

				Index(x=>x.UserName, FieldIndexing.Analyzed);
				Index(x => x.FirstLetterOfLastName, FieldIndexing.Analyzed);
			}
		}
	}
}