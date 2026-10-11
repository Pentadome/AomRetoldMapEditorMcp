// Reproduction written for tests: member access in a for-condition.
void scan()
{
   for (i = 0; i < gPlan.count; i = i + 1)
   {
      xsSetContextPlayer(1, 2, 3);
   }
}
