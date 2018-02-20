<Query Kind="FSharpProgram">
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <NuGetReference Version="1.0.1">FSharp.Collections.ParallelSeq</NuGetReference>
  <NuGetReference>FSharp.Core</NuGetReference>
  <NuGetReference>FSharp.Data</NuGetReference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <Namespace>FSharp.Data</Namespace>
  <Namespace>FSharp.Data.Runtime</Namespace>
  <Namespace>FSharp.Data.Runtime.BaseTypes</Namespace>
  <Namespace>FSharp.Data.Runtime.StructuralTypes</Namespace>
  <Namespace>FSharp.Data.Runtime.WorldBank</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>Newtonsoft.Json.Converters</Namespace>
</Query>

open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open FSharp.Data
open FSharp.Data.JsonExtensions
open FSharp.Collections.ParallelSeq
open Newtonsoft.Json.Converters

let musicDir = @"E:\Music"
let email = "email@domain.com"
let password = "passwd"

///http://www.fssnip.net/fy/title/Typeinference-friendly-division-and-multiplication
/// Floating point division given int and int args.
let (./.) x y = 
    (x |> double) / (y |> double)
    
//wrappers

let replace oldStr newStr (s : string) = 
  s.Replace(oldValue=oldStr, newValue=newStr)

let toLower (s : string) = s.ToLower() 

let getDirectory dir = 
    (new DirectoryInfo(dir)).EnumerateFiles("*.*", SearchOption.AllDirectories)

//end wrappers

type ibroadcast = {UserId:string; Token:string; Checksums:seq<string>; Supported:seq<string>}

let getMD5HashFromFile (file : FileInfo) =
    use fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024 * 1024)
    let md5 = MD5.Create()
    md5.ComputeHash fileStream
        |> BitConverter.ToString
        |> replace "-" ""
        |> toLower

let loadMediaFiles dir ( extensions: seq<string> ) =
    getDirectory dir
    |> Seq.filter (fun e -> extensions.Contains(e.Extension.ToLower()))

let uploadFile (url: string) userId token (file: FileInfo) = async {
    let filestream = new FileStream(file.FullName, FileMode.Open)
    let client = new HttpClient()

    let content = new MultipartFormDataContent()
    content.Add(new StringContent(userId), @"""user_id""")
    content.Add(new StringContent(token), @"""token""")
    content.Add(new StringContent(file.FullName), @"""file_path""")
    content.Add(new StringContent("f# uploader script"), @"""method""")
    content.Add(new StreamContent(filestream), @"""file""", sprintf @"""%s""" file.FullName)

    let result = client.PostAsync(url, content)
    let! response = Async.AwaitTask result
    let! content = Async.AwaitTask (response.Content.ReadAsStringAsync())
    return content
}


let uploadFiles (files : seq<FileInfo>) userId token (checksums:seq<string>) =
    let dc = new DumpContainer()
    dc.Dump()
    let contains md checksums = Seq.exists (fun elem -> elem = md) checksums
    let mutable count = 0
    let total = Seq.length files
    let isMissing file = 
            let md = getMD5HashFromFile file
            count <- count + 1
            dc.Content <- sprintf "%s\n (%d of %d)" file.FullName count total
            Util.Progress <- System.Nullable (int ((count ./. total)*100.0))
            not (contains md checksums)
    let missing = files |> Seq.filter isMissing
            
    [for file in missing -> 
        dc.Content <- (sprintf "uploading: %s" file.FullName)
        let result = uploadFile "https://sync.ibroadcast.com" userId token file |> Async.RunSynchronously
        printfn "%A %s: %A" DateTime.Now file.FullName result
        ]
    //|> Async.Parallel    

type Body = {
    mode: string
    email_address: string
    password: string
    version: int
    client: string
    supported_types: int
}  

let body = {
    mode = "status";
    email_address = email;
    password = password;
    version = 1;
    client = "f# uploader script";
    supported_types = 1
}

let login = async {
    let! value = Http.AsyncRequestString("https://json.ibroadcast.com/s/JSON/", headers = [ HttpRequestHeaders.ContentType HttpContentTypes.Json ], body = TextRequest (JsonConvert.SerializeObject body))
    let json = JsonValue.Parse(value)
    let supported = json?supported.AsArray() |> Seq.map (fun e -> e?extension.AsString())    
    let token = json?user?token.AsString()    
    let userId = json?user?id.AsString()    
    return (supported, token, userId)
} 

let getMD5List userId token = async {
    let! value = Http.AsyncRequestString("https://sync.ibroadcast.com", body = FormValues ["user_id", userId;"token", token])
    return JsonValue.Parse(value)?md5.AsArray()
        |> Seq.map (fun elem -> elem.AsString())
}

let (supported, token, userId) = login |> Async.RunSynchronously
let checksums = getMD5List userId token |> Async.RunSynchronously

uploadFiles (loadMediaFiles musicDir supported) userId token checksums |> ignore
