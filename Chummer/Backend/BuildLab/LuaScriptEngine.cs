using System;
using MoonSharp.Interpreter;

namespace Chummer.Backend.BuildLab
{
    public class LuaScriptEngine
    {
        public double EvaluateRule(string luaScript, string functionName, params object[] args)
        {
            try
            {
                Script script = new Script();
                script.DoString(luaScript);
                DynValue result = script.Call(script.Globals[functionName], args);
                return result.Number;
            }
            catch (InterpreterException ex)
            {
                throw new InvalidOperationException($"Lua script evaluation failed: {ex.Message}", ex);
            }
        }
    }
}
