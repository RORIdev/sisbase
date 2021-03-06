﻿using DSharpPlus;
using sisbase.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace sisbase.Utils
{
	/// <summary>
	/// The System Management Controller Now with 100% less PPBUS_G3H
	/// </summary>
	public class SMC
	{
		/// <summary>
		/// All of the current registerred systems on the SMC
		/// </summary>
		public static ConcurrentDictionary<Type, ISystem> RegisteredSystems { get; set; } = new ConcurrentDictionary<Type, ISystem>();

		/// <summary>
		/// All of the current registered timers on the SMC
		/// </summary>
		public static ConcurrentDictionary<Type, Timer> RegisteredTimers { get; set; } = new ConcurrentDictionary<Type, Timer>();

		internal static List<Assembly> RegisteredAssemblies { get; set; } = new List<Assembly>();

		internal Dictionary<Type, bool> RegisterSystems(Assembly assembly) {
			var instance = SisbaseBot.Instance;
			var response = new Dictionary<Type, bool>();
			if (!RegisteredAssemblies.Contains(assembly)) RegisteredAssemblies.Add(assembly);
			var Ts = assembly.ExportedTypes.Where(T => T.GetTypeInfo().IsSystemCandidate());
			foreach (var T in Ts)
			{
				if (RegisteredSystems.ContainsKey(T)) continue;
				if (instance.SystemCfg != null && instance.SystemCfg.Systems.ContainsKey(T.ToCustomName())) {
					if (!instance.SystemCfg.Systems[T.ToCustomName()].Enabled) {
						Logger.Warn("SMC",$"{T.ToCustomName()} was disabled on the config file.");
						response.Add(T,false);
						continue;
					}
				}
				if (T.GetInterfaces().Contains(typeof(IClientSystem)))
				{
					response.Add(T, SisbaseBot.Instance.Client.Register(T));
				}
				else
				{
					response.Add(T, Register(T));
				}
				if(assembly == typeof(SisbaseBot).Assembly) continue;
				if (Ts.Last() != T || instance.SystemCfg?.Systems.Count > RegisteredSystems.Count) continue;
				instance.SystemCfg?.Flush(); instance.SystemCfg?.Update();
			}
			return response;
		}

		internal Dictionary<Assembly, Dictionary<string, bool>> Reload()
		{
			var data = new Dictionary<Assembly, Dictionary<string, bool>>();
			foreach (var asm in RegisteredAssemblies)
			{
				var systems = RegisterSystems(asm);
				var nDict = systems.Select(x =>
				new KeyValuePair<string, bool>(
					x.Key.Name,
					x.Value)).ToDictionary(x => x.Key, x => x.Value);

				data.Add(asm, nDict);
			}
			ReloadCommands();
			return data;
		}

		internal static void ReloadCommands()
		{
			//Unregisters all commands
			SisbaseBot.Instance.CommandsNext.UnregisterCommands(SisbaseBot.Instance.CommandsNext.RegisteredCommands.Select(x => x.Value).ToArray());
			//Re-registers the commands
			RegisteredAssemblies.ForEach(x => SisbaseBot.Instance.CommandsNext.RegisterCommands(x));
		}

		internal bool Register(Type t)
		{
			if (RegisteredSystems.ContainsKey(t))
			{
				RegisteredSystems.TryGetValue(t, out var system);
				system.Warn("This system is already registered");
				return true;
			}
			else
			{
				var system = (ISystem)Activator.CreateInstance(t);

				system.Activate();
				system.Log("System Started");
				system.Execute();
				if (system.Status == true)
				{
					if (typeof(IScheduledSystem).IsAssignableFrom(t))
					{
						RegisteredTimers.TryAdd(t, CreateNewTimer(
							((IScheduledSystem)system).Timeout,
							((IScheduledSystem)system).RunContinuous
							));
						system.Log("Timer started");
					}
					RegisteredSystems.AddOrUpdate(t, system, (key, old) => system);
					system.Log("System Loaded");
					return true;
				}
				else
				{
					Logger.Warn("SMC", $"A system was unloaded.");
					return false;
				}
			}
		}

		//Todo : Better exception handling.
		internal static bool Unregister(Type t)
		{
			if (t.GetCustomAttribute(typeof(VitalAttribute)) != null)
			{
				Logger.Warn("SMC", "An vital system has attemped unregistering.");
				return false;
			}
			if (RegisteredSystems.ContainsKey(t))
			{
				ISystem system;
				RegisteredSystems.TryGetValue(t, out system);
				system.Warn("System is disabling...");
				if (typeof(IScheduledSystem).IsAssignableFrom(t))
				{
					RegisteredTimers[t].Dispose();
					RegisteredTimers.TryRemove(t, out _);
					system.Warn("Timer stopped");
				}
				system.Deactivate();
				RegisteredSystems.TryRemove(t, out system);
				Logger.Log("SMC", $"A System was disabled : {system.Name}");
				ReloadCommands();
				return true;
			}
			else
			{
				Logger.Warn("SMC", "An unregistered system has attemped unregistering.");
				return false;
			}
		}

		internal static Timer CreateNewTimer(TimeSpan timeout, Action action) =>
			new Timer(new TimerCallback(x => action()), null, TimeSpan.FromSeconds(1), timeout);
	}

	/// <summary>
	/// Provides extension methods for the <see cref="SMC"/>
	/// </summary>
	public static class SMCExtensions
	{
		internal static bool Register(this DiscordClient client, Type t)
		{
			if (SMC.RegisteredSystems.ContainsKey(t))
			{
				SMC.RegisteredSystems.TryGetValue(t, out var system);
				system.Warn("This system is already registered");
				return true;
			}
			else
			{
				var system = (IClientSystem)Activator.CreateInstance(t);

				system.Activate();
				system.Log("System Started");
				system.Execute();
				if (system.Status == true)
				{
					system.ApplyToClient(client);
					system.Log("System applied to client");
					if (typeof(IScheduledSystem).IsAssignableFrom(t))
					{
						SMC.RegisteredTimers.TryAdd(t, SMC.CreateNewTimer(
							((IScheduledSystem)system).Timeout,
							((IScheduledSystem)system).RunContinuous
							));
						system.Log("Timer started");
					}
					SMC.RegisteredSystems.AddOrUpdate(t, system, (key, old) => system);
					system.Log("System Loaded");
					return true;
				}
				else
				{
					Logger.Warn("SMC", $"A system was unloaded.");
					return false;
				}
			}
		}

		internal static bool IsSystemCandidate(this TypeInfo ti)
		{
			// check if compiler-generated
			if (ti.GetCustomAttribute<CompilerGeneratedAttribute>(false) != null)
				return false;

			// check if derives from the required base class
			var tmodule = typeof(ISystem);
			var timodule = tmodule.GetTypeInfo();
			if (!timodule.IsAssignableFrom(ti))
				return false;

			// check if anonymous
			if (ti.IsGenericType && ti.Name.Contains("AnonymousType") && (ti.Name.StartsWith("<>") || ti.Name.StartsWith("VB$")) && (ti.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic)
				return false;

			// check if abstract, static, or not a class
			if (!ti.IsClass || ti.IsAbstract)
				return false;

			// check if delegate type
			var tdelegate = typeof(Delegate).GetTypeInfo();
			if (tdelegate.IsAssignableFrom(ti))
				return false;

			// qualifies if any method or type qualifies
			return true;
		}
	}
}