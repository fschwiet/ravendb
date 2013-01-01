using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Abstractions.Indexing
{
	public enum SortFieldAggregation
	{
		UseInOrder,
		UseMinimum,
		UseMaximum,

		Default = UseInOrder
	}
}
