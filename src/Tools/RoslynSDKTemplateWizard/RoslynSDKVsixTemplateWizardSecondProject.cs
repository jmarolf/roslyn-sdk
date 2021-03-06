﻿using EnvDTE;

public class RoslynSDKVsixTemplateWizardSecondProject : RoslynSDKTestTemplateWizard
{
    public override void OnProjectFinishedGenerating(Project project)
    {
        base.OnProjectFinishedGenerating(project);

        // set the VSIX project to be the starting project
        var dte = project.DTE;
        if (dte.Solution.Projects.Count == 2)
        {
            dte.Solution.Properties.Item("StartupProject").Value = project.Name;
        }
    }
}
