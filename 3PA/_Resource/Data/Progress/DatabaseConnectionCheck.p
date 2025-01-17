DEFINE VARIABLE ipc_physicalName AS CHARACTER NO-UNDO.
DEFINE VARIABLE op-status AS CHARACTER NO-UNDO.
DEFINE VARIABLE i AS INTEGER NO-UNDO.
DEFINE STREAM str_out.
  ipc_physicalName = "{$LIST}".

  MESSAGE ipc_physicalName VIEW-AS ALERT-BOX.

OUTPUT STREAM str_out TO VALUE("{$DIR}" + "\postExecution.notif") APPEND BINARY.
DO i = 1 To NUM-ENTRIES(ipc_physicalName):
    PUT STREAM str_out UNFORMATTED STRING(STRING(CONNECTED(ipc_physicalName))) SKIP.
End.

OUTPUT STREAM str_out CLOSE.