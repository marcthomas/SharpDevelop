﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Controls;
using ICSharpCode.Core;
using ICSharpCode.Core.Presentation;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.TreeView;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Dom.ClassBrowser
{
	public class PersistedWorkspace
	{
		public PersistedWorkspace()
		{
			AssemblyFiles = new List<string>();
		}
		
		public string Name { get; set; }
		public List<String> AssemblyFiles { get; set; }
		public bool IsActive { get; set; }
	}
	
	class UnresolvedAssemblyEntityModelContext : IEntityModelContext
	{
		string assemblyName;
		string fullAssemblyName;
		string location;
		
		public UnresolvedAssemblyEntityModelContext(string assemblyName, string fullAssemblyName, string location)
		{
			this.assemblyName = assemblyName;
			this.fullAssemblyName = fullAssemblyName;
			this.location = location;
		}
		
		public ICompilation GetCompilation()
		{
			return null;
		}
		
		public bool IsBetterPart(IUnresolvedTypeDefinition part1, IUnresolvedTypeDefinition part2)
		{
			return false;
		}
		
		public IProject Project {
			get {
				return null;
			}
		}
		
		public string AssemblyName {
			get {
				return assemblyName;
			}
		}
		
		public string FullAssemblyName {
			get {
				return fullAssemblyName;
			}
		}
		
		public string Location {
			get {
				return location;
			}
		}
		
		public bool IsValid {
			get { return false; }
		}
	}
	
	class ClassBrowserPad : AbstractPadContent, IClassBrowser
	{
		#region IClassBrowser implementation

		public ICollection<IAssemblyList> AssemblyLists {
			get { return treeView.AssemblyLists; }
		}

		public IAssemblyList MainAssemblyList {
			get { return treeView.MainAssemblyList; }
			set { treeView.MainAssemblyList = value; }
		}
		
		public IAssemblyList UnpinnedAssemblies {
			get { return treeView.UnpinnedAssemblies; }
			set { treeView.UnpinnedAssemblies = value; }
		}
		
		public IAssemblyModel FindAssemblyModel(FileName fileName)
		{
			return treeView.FindAssemblyModel(fileName);
		}
		
		#endregion
		
		const string PersistedWorkspaceSetting = "ClassBrowser.Workspaces";
		const string DefaultWorkspaceName = "<default>";

		IProjectService projectService;
		ClassBrowserTreeView treeView;
		DockPanel panel;
		ToolBar toolBar;
		
		List<PersistedWorkspace> persistedWorkspaces;
		PersistedWorkspace activeWorkspace;

		public ClassBrowserPad()
			: this(SD.GetRequiredService<IProjectService>())
		{
		}
		
		public ClassBrowserPad(IProjectService projectService)
		{
			this.projectService = projectService;
			panel = new DockPanel();
			treeView = new ClassBrowserTreeView(); // treeView must be created first because it's used by CreateToolBar

			toolBar = CreateToolBar("/SharpDevelop/Pads/ClassBrowser/Toolbar");
			panel.Children.Add(toolBar);
			DockPanel.SetDock(toolBar, Dock.Top);
			
			panel.Children.Add(treeView);
			
			//treeView.ContextMenu = CreateContextMenu("/SharpDevelop/Pads/UnitTestsPad/ContextMenu");
			projectService.CurrentSolutionChanged += ProjectServiceCurrentSolutionChanged;
			ProjectServiceCurrentSolutionChanged(null, null);
			
			// Load workspaces from configuration
			treeView.Loaded += (sender, e) => LoadWorkspaces();
		}
		
		public override void Dispose()
		{
			projectService.CurrentSolutionChanged -= ProjectServiceCurrentSolutionChanged;
			base.Dispose();
		}
		
		public override object Control {
			get { return panel; }
		}
		
		public IClassBrowserTreeView TreeView {
			get { return treeView; }
		}
		
		public bool GoToEntity(IEntity entity)
		{
			// Activate the pad
			this.BringToFront();
			// Look for entity in tree
			return treeView.GoToEntity(entity);
		}
		
		public bool GotoAssemblyModel(IAssemblyModel assemblyModel)
		{
			// Activate the pad
			this.BringToFront();
			// Look for assembly node in tree
			return treeView.GotoAssemblyModel(assemblyModel);
		}
		
		void ProjectServiceCurrentSolutionChanged(object sender, EventArgs e)
		{
			foreach (var node in treeView.AssemblyLists.OfType<ISolutionAssemblyList>().ToArray())
				treeView.AssemblyLists.Remove(node);
			if (projectService.CurrentSolution != null)
				treeView.AssemblyLists.Add(new SolutionAssemblyList(projectService.CurrentSolution));
		}
		
		void AssemblyListCollectionChanged(IReadOnlyCollection<IAssemblyModel> removedItems, IReadOnlyCollection<IAssemblyModel> addedItems)
		{
			if (addedItems != null) {
				foreach (var assembly in addedItems) {
					// Add this assembly to current workspace
					if (activeWorkspace != null) {
						activeWorkspace.AssemblyFiles.Add(assembly.Context.Location);
					}
				}
			}
			
			if (removedItems != null) {
				foreach (var assembly in removedItems) {
					// Add this assembly to current workspace
					if (activeWorkspace != null) {
						activeWorkspace.AssemblyFiles.Remove(assembly.Context.Location);
					}
				}
			}
			
			// Update workspace list in configuration
			SaveWorkspaces();
		}
		
		/// <summary>
		/// Virtual method so we can override this method and return
		/// a dummy ToolBar when testing.
		/// </summary>
		protected virtual ToolBar CreateToolBar(string name)
		{
			Debug.Assert(treeView != null);
			return ToolBarService.CreateToolBar(treeView, treeView, name);
		}
		
		/// <summary>
		/// Virtual method so we can override this method and return
		/// a dummy ContextMenu when testing.
		/// </summary>
		protected virtual ContextMenu CreateContextMenu(string name)
		{
			Debug.Assert(treeView != null);
			return MenuService.CreateContextMenu(treeView, name);
		}
		
		/// <summary>
		/// Loads persisted workspaces from configuration.
		/// </summary>
		void LoadWorkspaces()
		{
			persistedWorkspaces = SD.PropertyService.GetList<PersistedWorkspace>(PersistedWorkspaceSetting).ToList();
			if (!persistedWorkspaces.Any())
			{
				// Add at least default workspace
				persistedWorkspaces = new List<PersistedWorkspace>();
				persistedWorkspaces.Add(new PersistedWorkspace()
				                        {
				                        	Name = DefaultWorkspaceName
				                        });
			}
			
			// Load all assemblies (for now always from default workspace)
			PersistedWorkspace defaultWorkspace = persistedWorkspaces.FirstOrDefault(w => w.Name == DefaultWorkspaceName);
			ActivateWorkspace(defaultWorkspace);
		}
		
		/// <summary>
		/// Stores currently saved workspaces in configuration.
		/// </summary>
		void SaveWorkspaces()
		{
			SD.PropertyService.SetList<PersistedWorkspace>(PersistedWorkspaceSetting, persistedWorkspaces);
		}
		
		static IAssemblyModel SafelyCreateAssemblyModelFromFile(string fileName)
		{
			var modelFactory = SD.GetRequiredService<IModelFactory>();
			try {
				return SD.AssemblyParserService.GetAssemblyModel(new FileName(fileName), true);
			} catch (Exception) {
				// Special AssemblyModel for unresolved file references
				string fakedAssemblyName = Path.GetFileName(fileName);
				IEntityModelContext unresolvedContext = new UnresolvedAssemblyEntityModelContext(fakedAssemblyName, fakedAssemblyName, fileName);
				IUpdateableAssemblyModel unresolvedModel = modelFactory.CreateAssemblyModel(unresolvedContext);
				unresolvedModel.AssemblyName = unresolvedContext.AssemblyName;
				unresolvedModel.FullAssemblyName = unresolvedContext.FullAssemblyName;
				
				return unresolvedModel;
			}
		}
		
		void AppendAssemblyFileToList(string assemblyFile)
		{
			IAssemblyModel assemblyModel = SafelyCreateAssemblyModelFromFile(assemblyFile);
			if (assemblyModel != null) {
				MainAssemblyList.Assemblies.Add(assemblyModel);
			}
		}
		
		/// <summary>
		/// Activates the specified workspace.
		/// </summary>
		void ActivateWorkspace(PersistedWorkspace workspace)
		{
			// Update the activation flags in workspace list
			foreach (var workspaceElement in persistedWorkspaces) {
				workspaceElement.IsActive = (workspaceElement == workspace);
			}
			
			UpdateActiveWorkspace();
		}
		
		/// <summary>
		/// Updates active workspace and AssemblyList according to <see cref="PersistedWorkspace.IsActive"/> flags.
		/// </summary>
		void UpdateActiveWorkspace()
		{
			if ((MainAssemblyList != null) && (activeWorkspace != null)) {
				// Temporarily detach from event handler
				MainAssemblyList.Assemblies.CollectionChanged -= AssemblyListCollectionChanged;
			}
			
			activeWorkspace = persistedWorkspaces.FirstOrDefault(w => w.IsActive);
			if (activeWorkspace == null) {
				// If no workspace is active, activate default
				var defaultWorkspace = persistedWorkspaces.FirstOrDefault(w => w.Name == DefaultWorkspaceName);
				activeWorkspace = defaultWorkspace;
				defaultWorkspace.IsActive = true;
			}
			
			MainAssemblyList.Assemblies.Clear();
			if (activeWorkspace != null) {
				foreach (string assemblyFile in activeWorkspace.AssemblyFiles) {
					AppendAssemblyFileToList(assemblyFile);
				}
			}
			
			// Attach to event handler, again.
			if (MainAssemblyList != null) {
				MainAssemblyList.Assemblies.CollectionChanged += AssemblyListCollectionChanged;
			}
		}
	}
}
