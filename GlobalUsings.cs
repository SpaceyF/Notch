// wpf and winforms both have an "application" type, so a plain "application" is
// ambiguous. this app is wpf, so point it there. winforms stuff uses the winforms. alias.
global using Application = System.Windows.Application;
