namespace AreaMapTilesTextConv
{
	public static class Utility
	{
		public static bool AllNull(this byte[] me)
		{
			if (me == null)
			{
				return true;
			}

			if (me.Length == 0)
			{
				return true;
			}

			for (int n = 0, len = me.Length; n < len; ++n)
			{
				if (me[n] != 0)
				{
					return false;
				}
			}

			return true;
		}
	}
}