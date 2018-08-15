# CSharpMutation

Mark Hays

Direct Supply/Rose-Hulman Institute of Technology

A fast mutation testing plugin for C#, originally built for Visual Studio Enterprise 2015. It features many of the code coverage optimizations first introduced by pitest for Java. 

Also tested Community and Enterprise flavors of 2017; seems to work.

## Installation

Run this VSIX: https://github.com/mahays0/CSharpMutation/raw/master/release/CSharpMutation_VSIX.vsix 

## Usage

The extension adsd a new menu item to Visual Studio, **Tools -> Invoke CSharpMutation**. When you first start Visual Studio, give Visual Studio a minute to fully load your tests, otherwise the tool's project selection dialog will be blank at first. (if this happens, just close the dialog and try again)

The mutation testing runs in the background. While  you wait, you can look at live mutants as they appear in the VS built-in Error List (they appear as Warnings). Killed mutants will appear after testing is complete (they appear as Messages). You can double-click a mutant in the Error List to take you to its original in the code.

## Performance optimizations
This tool features several performance optimizations that, from what I gather, most authors of C# mutation tools have not yet tried:
 - Mutants are compiled and tested entirely **in memory** using Roslyn and a simulation of the MSTest semantics. Your hard disk is not filled to the brim with mutants like other tools.
 - CSharpMutation runs in **one process**. It uses AppDomains, like a web server does, to isolate mutants.
 - CSharpMutation **preloads** all of your project's dependencies exactly once into memory, so they are not endlessly loaded/unloaded between mutants.

CSharpMutation also includes the usual performance optimizations that pitest is famous for:
- Uses code coverage to run only relevant tests on mutants.
- Short-circuits mutant generation if the statement deletion mutant lives.
