using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using Lucene.Net.Search;
using Raven.Json.Linq;

namespace Raven.Database.Plugins
{
	[InheritedExport]
	public abstract class AbstractFilterGenerator
	{
		public abstract string GetName();
		public abstract Filter Create(RavenJArray filterArguments);
	}
}
