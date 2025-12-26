using System;
using System.Collections;
using System.Collections.Generic;

public static class Tool
{
    public static void CheckFunc(object func)
    {
        if (func is not Delegate del)
        {
            throw new ArgumentException("func must be a delegate");
        }

        var method = del.Method;
        var parameters = method.GetParameters();

        if (parameters.Length < 1 || parameters.Length > 2)
        {
            throw new ArgumentException("callback must have 1 or 2 parameters");
        }

        if (!typeof(IContext).IsAssignableFrom(parameters[0].ParameterType))
        {
            throw new ArgumentException("callback first parameter must be IContext");
        }

        if (parameters.Length == 2)
        {
            var secondParamType = parameters[1].ParameterType;
            bool isAllowed =
                secondParamType == typeof(string) ||
                secondParamType == typeof(byte[]) ||
                secondParamType == typeof(Dictionary<string, object>) ||
                (secondParamType.IsClass && !secondParamType.IsAbstract && !typeof(IEnumerable).IsAssignableFrom(secondParamType));
            if (!isAllowed)
            {
                throw new ArgumentException("callback second parameter must be string, byte[], Dictionary<string, object> or a class type");
            }
        }
    }
}
