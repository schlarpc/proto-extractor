using System.Collections.Generic;
using System.Text;

namespace extractor.src.util
{
	class StringComparer : IComparer<string>
	{
		public int Compare(string x, string y)
		{
			var xBytes = Encoding.ASCII.GetBytes(x);
			var yBytes = Encoding.ASCII.GetBytes(y);

			for (int i = 0; i < xBytes.Length && i < yBytes.Length; i++)
			{
				if (xBytes[i] < yBytes[i])
				{
					return -1;
				}
				else if (xBytes[i] > yBytes[i])
				{
					return 1;
				}
			}

			if (xBytes.Length == yBytes.Length)
			{
				return 0;
			}
			else if (xBytes.Length < yBytes.Length)
			{
				return -1;
			}
			else
			{
				return 1;
			}
		}
	}
}
