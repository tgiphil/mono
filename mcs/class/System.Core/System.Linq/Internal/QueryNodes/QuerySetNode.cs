#if NET_4_0
//
// QueryMuxNode.cs
//
// Author:
//       Jérémie "Garuma" Laval <jeremie.laval@gmail.com>
//
// Copyright (c) 2010 Jérémie "Garuma" Laval
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace System.Linq
{
	internal class QuerySetNode<TSource> : QueryMuxNode<TSource, TSource, TSource>
	{
		readonly SetInclusion setInclusion;
		readonly IEqualityComparer<TSource> comparer;

		internal QuerySetNode (SetInclusion setInclusion, IEqualityComparer<TSource> comparer,
		                       QueryBaseNode<TSource> first, QueryBaseNode<TSource> second)
			: base (first, second)
		{
			this.setInclusion = setInclusion;
			this.comparer = comparer;
		}

		internal override IEnumerable<TSource> GetSequential ()
		{
			IEnumerable<TSource> first = Parent.GetSequential ();
			IEnumerable<TSource> second = Second == null ? null : Second.GetSequential ();

			// We try to do some guessing based on the default
			switch (setInclusion) {
			case SetInclusionDefaults.Union:
				return first.Union (second, comparer);
			case SetInclusionDefaults.Intersect:
				return first.Intersect (second, comparer);
			case SetInclusionDefaults.Except:
				return first.Except (second, comparer);
			case SetInclusionDefaults.Distinct:
				return first.Distinct (comparer);
			}

			// Default is we return the bare source enumerable
			return first;
		}

		internal override IList<IEnumerable<TSource>> GetEnumerables (QueryOptions options)
		{
			IList<IEnumerable<TSource>> first = Parent.GetEnumerables (options);
			IList<IEnumerable<TSource>> second = Second.GetEnumerables (options);
			
			IEnumerable<TSource>[] result = new IEnumerable<TSource>[first.Count];
			ConcurrentSkipList<TSource> checker = new ConcurrentSkipList<TSource> (comparer);
			
			InitConcurrentSkipList (checker, second, (e) => e);

			for (int i = 0; i < result.Length; i++)
				result[i] = GetEnumerable<TSource> (first[i], second[i], checker, (e) => e);

			return result;
		}

		internal override IList<IEnumerable<KeyValuePair<long, TSource>>> GetOrderedEnumerables (QueryOptions options)
		{
			IList<IEnumerable<KeyValuePair<long, TSource>>> first = Parent.GetOrderedEnumerables (options);
			IList<IEnumerable<KeyValuePair<long, TSource>>> second = Second.GetOrderedEnumerables (options);

			IEnumerable<KeyValuePair<long, TSource>>[] result = new IEnumerable<KeyValuePair<long, TSource>>[first.Count];
			ConcurrentSkipList<TSource> checker = new ConcurrentSkipList<TSource> (comparer);
			
			InitConcurrentSkipList (checker, second, (e) => e.Value);

			for (int i = 0; i < result.Length; i++)
				result[i] = GetEnumerable<KeyValuePair<long, TSource>> (first[i], second[i], checker, (e) => e.Value);

			return result;
		}
				
		void InitConcurrentSkipList<TExtract> (ConcurrentSkipList<TSource> checker,
		                                       IList<IEnumerable<TExtract>> feeds,
		                                       Func<TExtract, TSource> extractor)
		{
			if ((setInclusion & SetInclusion.Preload) == 0)
				return;
			
			foreach (IEnumerable<TExtract> feed in feeds)
				foreach (TExtract item in feed)
					checker.TryAdd (extractor (item));
		}

		IEnumerable<TExtract> GetEnumerable<TExtract> (IEnumerable<TExtract> first,
		                                               IEnumerable<TExtract> second,
		                                               ConcurrentSkipList<TSource> checker,
		                                               Func<TExtract, TSource> extractor)
		{
			IEnumerator<TExtract> eFirst = first.GetEnumerator ();
			IEnumerator<TExtract> eSecond = second == null ? null : second.GetEnumerator ();

			IEnumerator<TExtract> current = eFirst;
			bool outInclusion = (setInclusion & SetInclusion.Out) > 0;
			bool preload = (setInclusion & SetInclusion.Preload) > 0;
			bool relaxed = (setInclusion & SetInclusion.Relaxed) > 0;

			while (current != null) {
				while (current.MoveNext ()) {
					bool result = relaxed ?
						checker.Contains (extractor (current.Current)) : checker.TryAdd (extractor (current.Current));

					if ((result && outInclusion)
					    || (!result && !outInclusion))
						yield return current.Current;
				}

				if (current == eFirst && !preload)
					current = eSecond;
				else
					break;
			}
		}
	}
}

#endif
