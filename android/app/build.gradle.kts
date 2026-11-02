plugins { id("com.android.application"); id("org.jetbrains.kotlin.android") }
android {
    namespace = "app.clipbridge"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig { applicationId = "app.clipbridge"; minSdk = 29; targetSdk = 36; versionCode = 40000; versionName = "0.4.0" }
    val releaseKeystore = providers.environmentVariable("CLIPBRIDGE_ANDROID_KEYSTORE_PATH").orNull
    if (releaseKeystore != null) {
        signingConfigs {
            create("publish") {
                storeFile = file(releaseKeystore)
                storePassword = providers.environmentVariable("CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD").get()
                keyAlias = "clipbridge"
                keyPassword = storePassword
            }
        }
        buildTypes.getByName("release") { signingConfig = signingConfigs.getByName("publish") }
    }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    testOptions { unitTests.isReturnDefaultValues = true }
}
dependencies { testImplementation("junit:junit:4.13.2"); testImplementation("org.json:json:20240303") }
