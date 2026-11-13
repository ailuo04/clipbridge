plugins { id("com.android.application"); id("org.jetbrains.kotlin.android") }
android {
    namespace = "app.clipbridge"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig { applicationId = "app.clipbridge"; minSdk = 29; targetSdk = 36; versionCode = 60100; versionName = "0.6.1" }
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
dependencies {
    implementation("androidx.core:core:1.13.1")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("com.google.zxing:core:3.5.3")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}

tasks.withType<org.gradle.api.tasks.testing.Test>().configureEach {
    inputs.property("clipbridgeFolderInterop", providers.environmentVariable("CLIPBRIDGE_FOLDER_INTEROP_DIR").orElse(""))
    inputs.property("clipbridgeLegacyInterop", providers.environmentVariable("CLIPBRIDGE_INTEROP_DIR").orElse(""))
    inputs.property("clipbridgeExperienceInterop", providers.environmentVariable("CLIPBRIDGE_EXPERIENCE_INTEROP_DIR").orElse(""))
}
