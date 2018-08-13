# CSharpMutation
Mutation testing plugin for Visual Studio Enterprise 2015.

Also tested Community and Enterprise flavors of 2017; seems to work.

## Installation

Run this VSIX: https://github.com/mahays0/CSharpMutation/raw/master/release/CSharpMutation_VSIX.vsix 

It will add a new menu item, Tools -> Invoke CSharpMutation. When you first start Visual Studio, give it a minute to fully load your tests, otherwise the tool's project selection dialog will be blank at first.

The mutation testing runs in the background. While  you wait, you can look at live mutants as they appear in the VS built-in Error List (they appear as Warnings). Killed mutants will appear after testing is complete (they appear as Messages).
