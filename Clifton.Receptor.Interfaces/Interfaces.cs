﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Clifton.SemanticTypeSystem.Interfaces;

namespace Clifton.Receptor.Interfaces
{
	/// <summary>
	/// All receptors must implement this interface in order to be dynamically loaded at runtime.
	/// </summary>
	public interface IReceptorInstance
	{
		string Name { get; }
		bool IsEdgeReceptor { get; }
		bool IsHidden { get; }
		string[] GetReceiveCarriers();
		void ProcessCarrier(ISemanticTypeStruct protocol, dynamic signal);
		void Terminate();
	}

	public interface IReceptorSystem
	{
		ISemanticTypeSystem SemanticTypeSystem { get; set; }
		void CreateCarrier(IReceptorInstance from, ISemanticTypeStruct protocol, dynamic signal, bool stopRecursion = false);
	}

	public interface ICarrier
	{
		ISemanticTypeStruct Protocol { get; set; }
		dynamic Signal { get; set; }
	}

	public interface IReceptor
	{
		string Name { get; }
		IReceptorInstance Instance { get; }
	}
}
