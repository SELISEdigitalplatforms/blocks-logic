using System.Reflection;
using Blocks.Genesis;

namespace XUnitTest
{
    // Invokes BlocksContext.Create through reflection while tolerating growth of its
    // parameter list. Any parameters the caller does not supply are filled with the
    // parameter's default value, so the test helpers stay compatible when Genesis adds
    // new optional parameters to Create.
    internal static class BlocksContextTestExtensions
    {
        public static BlocksContext CreateContext(this MethodInfo method, object[] args)
        {
            var parameters = method.GetParameters();
            var fullArgs = new object[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                if (i < args.Length)
                {
                    fullArgs[i] = args[i];
                }
                else if (parameters[i].HasDefaultValue)
                {
                    fullArgs[i] = parameters[i].DefaultValue;
                }
                else if (parameters[i].ParameterType.IsValueType)
                {
                    fullArgs[i] = Activator.CreateInstance(parameters[i].ParameterType);
                }
                else
                {
                    fullArgs[i] = null;
                }
            }

            return (BlocksContext)method.Invoke(null, fullArgs);
        }
    }
}
