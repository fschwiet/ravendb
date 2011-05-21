using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Search;
using Raven.Json.Linq;

namespace Raven.Database.Plugins
{
    public abstract class AbstractFilter : IRequiresDocumentDatabaseInitialization
    {
        public abstract void Initialize(DocumentDatabase database);
        public abstract string GetName();
        public abstract Filter Create(RavenJArray parameters);
    }
}
