// Reproduction written for tests: rule without active/inactive, unknown call, unbalanced brace.
rule watch
{
   undefinedHelper(1);
}
void broken()
{
   xsEnableRule("watch");
