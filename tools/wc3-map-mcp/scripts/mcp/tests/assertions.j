function HTW_Test_Assert takes nothing returns nothing
    call HTW_Debug_LogText("test assertion hook ready")
endfunction

function HTW_Test_AssertBoolean takes boolean condition, string label returns nothing
    if condition then
        call HTW_Debug_LogText("scenario=" + label + " result=pass")
    else
        call HTW_Debug_LogText("scenario=" + label + " result=fail")
    endif
endfunction
