// Reproduction written for tests: extern in an included file.
include "inc_extern.xs";
void ok()
{
   gShared = 1;
}
