#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
// <copyright file="NativeFuture.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

namespace Google.Impl {
  using System;
    using System.Runtime.InteropServices;

  /// <summary>
  /// Native future is an interal class that implements the FutureAPIImpl
  /// by calling native methods which are implemented in the native code.
  /// </summary>
  internal class NativeFuture : BaseObject, FutureAPIImpl<GoogleSignInUser> {

    internal NativeFuture(IntPtr ptr) : base(ptr) {
    }

    public override void Dispose() => GoogleSignInViaCredentialsX.GoogleSignIn_DisposeFuture(SelfPtr());

    public bool Pending => GoogleSignInViaCredentialsX.GoogleSignIn_Pending(SelfPtr());

    public GoogleSignInUser Result {
      get {
        HandleRef self = SelfPtr();
        IntPtr ptr = GoogleSignInViaCredentialsX.GoogleSignIn_Result(self);
        if (ptr == IntPtr.Zero) {
          return null;
        }

        var user = new GoogleSignInUser();
        var userPtr = new HandleRef(user, ptr);

        user.UserId = GoogleSignInViaCredentialsX.GoogleSignIn_GetUserId(userPtr);

        user.Email = GoogleSignInViaCredentialsX.GoogleSignIn_GetEmail(userPtr);

        user.DisplayName = GoogleSignInViaCredentialsX.GoogleSignIn_GetDisplayName(userPtr);

        user.FamilyName = GoogleSignInViaCredentialsX.GoogleSignIn_GetFamilyName(userPtr);

        user.GivenName = GoogleSignInViaCredentialsX.GoogleSignIn_GetGivenName(userPtr);

        user.IdToken = GoogleSignInViaCredentialsX.GoogleSignIn_GetIdToken(userPtr);

        user.AuthCode = GoogleSignInViaCredentialsX.GoogleSignIn_GetServerAuthCode(self);

        string url = GoogleSignInViaCredentialsX.GoogleSignIn_GetImageUrl(userPtr);
        if (url?.Length > 0) {
          user.ImageUrl = new System.Uri(url);
        }

#pragma warning disable CS0219 // Require for no reason (tree shaking ?)
        var obj = (user.UserId,user.Email,user.DisplayName,user.FamilyName,user.GivenName,user.IdToken,user.AuthCode,user.ImageUrl);
#pragma warning restore CS0219

        return user;
      }
    }

    /// <summary>
    /// Gets the status.
    /// </summary>
    /// <remarks>The platform specific implementation maps the platform specific
    /// code to one defined in GoogleSignStatusCode.</remarks>
    /// <value>The status.</value>
    public GoogleSignInStatusCode Status {
      get {
        return (GoogleSignInStatusCode)GoogleSignInViaCredentialsX.GoogleSignIn_Status(SelfPtr());
      }
    }
  }
}
#endif
