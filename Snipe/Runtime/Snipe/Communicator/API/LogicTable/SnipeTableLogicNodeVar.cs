
namespace MiniIT.Snipe
{
	public class SnipeTableLogicRawNodeVar
	{
		public string type;
		public string name;
		public string @operator;
		public int value;
		public float condValue;
	}
	
	public class SnipeTableLogicNodeVar
	{
		public const string TYPE_ATTR = "attr";
		public const string TYPE_CLIENT = "client";
		public const string TYPE_COUNTER = "counter";
		public const string TYPE_COND_COUNTER = "condCounter";
		
		public string type;
		public string name;
		
		public static implicit operator SnipeTableLogicNodeVar(SnipeTableLogicRawNodeVar raw)
		{
			SnipeTableLogicNodeVar result = null;
			
			switch (raw?.type)
			{
				case TYPE_ATTR:
					result = new SnipeTableLogicNodeVarAttr()
					{
						type = raw.type,
						name = raw.name,
					};
					break;
				
				case TYPE_CLIENT:
					result = new SnipeTableLogicNodeVarClient()
					{
						type = raw.type,
						name = raw.name,
						value = raw.value,
					};
					break;
				
				case TYPE_COUNTER:
					result = new SnipeTableLogicNodeVarCounter()
					{
						type = raw.type,
						name = raw.name,
					};
					break;
					
				case TYPE_COND_COUNTER:
					result = new SnipeTableLogicNodeVarCondCounter()
					{
						type = raw.type,
						name = raw.name,
						@operator = raw.@operator,
						condValue = raw.condValue,
					};
					break;
			}
			
			return result;
		}
	}
	
	public class SnipeTableLogicNodeVarAttr : SnipeTableLogicNodeVar
	{
	}
	
	public class SnipeTableLogicNodeVarClient : SnipeTableLogicNodeVar
	{
		public int value;
	}
	
	public class SnipeTableLogicNodeVarCounter : SnipeTableLogicNodeVar
	{
	}
	
	public class SnipeTableLogicNodeVarCondCounter : SnipeTableLogicNodeVar
	{
		public string @operator;
		public float condValue;
	}
}