using System.Collections.Concurrent;
using System.Reflection;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// An <see cref="IDatabase"/> that records every call by method name and answers from
    /// per-method handlers. Used instead of Moq for the reliability tests because
    /// StackExchange.Redis overloads (StreamAddAsync, StringSetAsync…) differ only by optional
    /// parameters, which Moq expression setups cannot express without pinning one exact overload
    /// — a test that silently stopped matching after a Redis client upgrade would prove nothing.
    /// Anything without a handler returns a completed task with the default value.
    /// </summary>
    public class FakeRedisDatabase : DispatchProxy
    {
        private ConcurrentQueue<(string Method, object?[] Args)> _calls = new();
        private ConcurrentDictionary<string, Func<object?[], object?>> _handlers = new(StringComparer.Ordinal);

        public static (IDatabase Database, FakeRedisDatabase Fake) Create()
        {
            var proxy = Create<IDatabase, FakeRedisDatabase>();
            return (proxy, (FakeRedisDatabase)(object)proxy);
        }

        /// <summary>Answers calls to <paramref name="method"/> (any overload) with <paramref name="handler"/>'s value, unwrapped.</summary>
        public FakeRedisDatabase On(string method, Func<object?[], object?> handler)
        {
            _handlers[method] = handler;
            return this;
        }

        public IReadOnlyList<object?[]> Calls(string method) =>
            _calls.Where(c => c.Method == method).Select(c => c.Args).ToList();

        public IReadOnlyList<string> CallNames => _calls.Select(c => c.Method).ToList();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            args ??= [];
            _calls.Enqueue((targetMethod.Name, args));

            object? value = null;
            var handled = false;
            if (_handlers.TryGetValue(targetMethod.Name, out var handler))
            {
                // Throwing handlers model Redis failures; for async methods the exception is
                // surfaced as a faulted task, as the real client does.
                try
                {
                    value = handler(args);
                    handled = true;
                }
                catch (Exception ex) when (IsTask(targetMethod.ReturnType))
                {
                    return Faulted(targetMethod.ReturnType, ex);
                }
            }

            return Wrap(targetMethod.ReturnType, handled ? value : null);
        }

        private static bool IsTask(Type type) => typeof(Task).IsAssignableFrom(type);

        private static object? Wrap(Type returnType, object? value)
        {
            if (returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                var result = value ?? Default(inner);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
            }
            return value ?? Default(returnType);
        }

        private static object Faulted(Type returnType, Exception ex)
        {
            if (returnType == typeof(Task)) return Task.FromException(ex);
            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethods()
                .Single(m => m.Name == nameof(Task.FromException) && m.IsGenericMethod)
                .MakeGenericMethod(inner).Invoke(null, [ex])!;
        }

        private static object? Default(Type type)
        {
            if (type == typeof(RedisValue)) return RedisValue.Null;
            if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
