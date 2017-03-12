<%@ Page Language="C#" %>

<%
    Response.Write("Version: " + System.Environment.Version.ToString());

    Response.Write("<br/>UsingIntegratedPipeline=" + System.Web.HttpRuntime.UsingIntegratedPipeline.ToString());
    Response.Write(", IISVersion=" + System.Web.HttpRuntime.IISVersion.ToString());
    Response.Write("<br/>AspInstallDirectory=" + System.Web.HttpRuntime.AspInstallDirectory.ToString());
    Response.Write("<br/>AppDomain.BaseDirectory=" + System.AppDomain.CurrentDomain.BaseDirectory);

    try {
        // RazorGenerator.Mvc.EngineDebug.Output(Response);
    }
    catch { ; }

    Response.Write("<br/>Assemblies=" + System.AppDomain.CurrentDomain.GetAssemblies().Length.ToString());

    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
        try {
            Response.Write("<br/>" + asm.CodeBase.Replace("file:///", ""));
        }
        catch {
            // if Anonymously Hosted DynamicMethods Assembly
            Response.Write("<br/>" + asm.FullName);
        }
    }
          
%>