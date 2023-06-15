IF|DO|DO WHILE|FOR EACH|CREATE|COMMENT~
+
FOR EACH {&name} WHERE X = Y NO-LOCK: //{&name} Start

|||

END. //{&name} End
 
+
DO WHILE i <= X NO-UNDO: //{&name} Start

|||

END. //{&name} End

+
CREATE {&name}.
ASSIGN {&name}.field1 = "test"
       {&name}.field2 = "test"
. //End Assign
|||
+
IF X = Y THEN DO:

|||

END.
ELSE DO:

END.
+
DO i = 0 TO x: //{&name} Start
   
|||
   
END. //{&name} END
+
/* Comment {&name} Start*/ #Comment#
|||
/* Comment {&name} End*/
