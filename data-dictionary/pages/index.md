---
title: Welcome to Wintap Data!
---

### **Overview of LLNL Wintap and Datasets**

[Access Data Here](https://gdo168.llnl.gov/data/)

[Data Dictionary](datadict)

[**Wintap**](https://github.com/LLNL/Wintap) is an open-source host-based telemetry collection tool developed by **Lawrence Livermore National Laboratory (LLNL)**. It is designed to address the challenges of collecting, processing and analyzing host-based data in large-scale Windows environments, specifically to support cybersecurity research.

The [**ACME datasets**](https://gdo168.llnl.gov) are realistic, high-quality cybersecurity datasets generated using **Wintap** in simulated Windows business network environments. These datasets are designed to support hands-on learning, innovative research, and practical experience in cybersecurity. These datasets are released under the Creative Commons 4.0 license.

**Wintap** ([GitHub](https://github.com/LLNL/Wintap)) and the [**ACME datasets**](https://gdo168.llnl.gov) are powerful tools for advancing cybersecurity research and operations. Wintap's granular data collection and flexible processing pipeline make it a valuable tool for studying cyber-attacks and developing detection tools. While ACME datasets provide realistic, labeled data for studying cyber-attacks and defenses. Together, they form a comprehensive framework for improving cybersecurity practices and fostering innovation in the field.

In addition to the ACME datasets, we also have the [**Dynamic Malware Behavior Dataset**](DMBD). This dataset is derived from 65,416 software samples with comprehensive execution logs. It can be used for training and evaluating machine learning models to distinguish between malicious and benign software based on their runtime behavior, with baseline accuracy of 95% that researchers are challenged to surpass.

---

#### **Why Wintap?**
In large-scale Windows environments, organizations frequently need to collect and analyze host-based data for:
- IT operations.
- Cybersecurity response.
- Cyber research.

Traditional approaches rely on **one-off scripts, utilities, or single-purpose agents**, which lead to:
1. **Agent Bloat**:
   - Deploying multiple tools to endpoints increases resource consumption, making systems inefficient.
2. **Inconsistent Data Models**:
   - Windows APIs describe the same data in inconsistent ways (e.g., processes identified by name, PID, or thread IDs; users identified by SID, GUID, or username).
   - This inconsistency makes datasets difficult to reuse or discover for other purposes.
3. **Scalability Issues**:
   - As data collection needs grow, maintaining multiple tools becomes unsustainable.
4. **Complexity of Windows APIs**:
   - Windows offers a myriad of APIs for event data, but their complexity makes development challenging.

---

#### **Key Features of Wintap**
Wintap solves these challenges with the following capabilities:

| **Feature**                     | **Description**                                                                 |
|----------------------------------|---------------------------------------------------------------------------------|
| **Singular, Extensible Agent**   | Consolidates data collection into a single agent footprint, avoiding "agent bloat." |
| **Extensibility**                | Developers can add new functionality via independent library modules using an easy-to-use API. |
| **Unified Data Model**           | Standardized representation of host-based data enables consistency, reusability, and correlation. |
| **API Abstraction**              | Simplifies access to low-level Windows APIs like **ETW** and **COM+**. Plugin authors can leverage these APIs without implementing low-level details. |
| **Real-Time Analytics**          | Includes a locally hosted **web-based analytic workbench** for querying and exploring live event streams. |
| **Scalable Data Processing**     | Efficiently processes large-scale telemetry data in stages (Raw, Rolling, Standard View). |

---

#### **How Wintap Works**
1. **Data Collection**:
   - Wintap collects granular telemetry data from Windows systems, including:
     - **Process activity**: Creation, termination, and behavior of processes.
     - **Network activity**: Connections made by processes.
     - **File activity**: Read/write operations on files.
     - **Registry activity**: Modifications and interactions with the Windows registry.
     - **DLL activity**: Dynamic link library (DLL) loads.
     - **Other host-level data**: Cursor focus changes, wait times, and performance metrics.
   - Data is written to small files every minute for real-time collection.

2. **Data Processing Pipeline**:
   Wintap processes the collected data in three stages:
   - **Raw Data**:
     - Captures detailed, event-based data at the lowest level of granularity.
     - Useful for streaming analytics or simulating live data collection.
   - **Rolling Data**:
     - De-duplicated and summarized data for a single day.
     - Optimized for daily analysis but may have duplicates across multiple days.
   - **Standard View**:
     - Summarized data spanning multiple days or months, tailored for specific research tasks.

3. **Data Storage**:
   - Data is stored in **Parquet format**, which is efficient for large-scale analytics.
   - Files are partitioned by time (e.g., daily) to facilitate easier querying and processing.

4. **Data Discovery and Exploration**:
   - Users can explore live event streams and query data in real time using the **web-based analytic workbench**.

---

#### **Applications of Wintap**
- **Cybersecurity Research**:
  - Wintap is used to generate realistic datasets (e.g., ACME datasets) for studying cyber-attacks and defenses.
  - These datasets are critical for training machine learning models, developing detection tools, and studying attacker behavior.
- **IT Operations**:
  - Provides visibility into system activity for troubleshooting and performance monitoring.
- **Cybersecurity Response**:
  - Enables real-time monitoring and analysis of host-based activity, aiding in incident detection and response.

---

### **ACME Datasets**
#### **Overview**
The [**ACME datasets**](https://gdo168.llnl.gov/data) are realistic, high-quality cybersecurity datasets generated using **Wintap** in simulated Windows business network environments. These datasets are designed to support hands-on learning, innovative research, and practical experience in cybersecurity.

---

#### **Purpose**
The ACME datasets aim to address the challenges of sharing real-world cybersecurity data due to privacy and security concerns. By collecting data from controlled environments, ACME provides researchers with:
- High-quality, realistic datasets for studying cyber-attacks and defenses.
- Data tailored to specific research needs, from small, focused datasets to massive, all-encompassing datasets.
- A shareable reference data set that can be used as the basis for publications and papers.
---

#### **Environment for ACME Datasets**
- Simulated Windows business network with:
  - Windows Active Directory Server
  - 10–22 Windows workstations
  - Additional services like file servers and chat servers.
- Participants engage in cyber-attack and defense scenarios to generate data.

---

#### **ACME Datasets**
| **Dataset** | **Workstations** | **Duration** | **Labeled Attacks**                          | **Size**      | **Notes**                                                                 |
|-------------|------------------|--------------|-----------------------------------------------|---------------|---------------------------------------------------------------------------|
| **ACME 3**  | 22               | 2 weeks      | Volt Typhoon, Caldera, Metasploit, others    | 14 GB (Parquet) | Standard logging level.                                                   |
| **ACME 4**  | 10               | 2 weeks      | Living off the Land (LoL), Caldera, Metasploit, others | 13 GB (Parquet) | Increased logging level compared to ACME 3.                              |

---

#### **Labeling Methods in ACME Datasets**
1. **Manual Labels**:
   - Created by collaborating with red team attackers.
   - Activity graphs (processes, files, network, etc.) are developed and stored in **NetworkX JSON format**.
   - Summarized into tables (e.g., `labels_graph_process_summary.parquet`) for analysis.

2. **Scripted Labels**:
   - **Sigma/Mitre**: Publicly available rules are applied, though they may produce false positives.
   - **LolBAS (Living off the Land Binaries and Scripts)**: Matches processes with known LoL binaries/scripts, also prone to false positives.

| **Label Type** | **File**                          | **Description**                                                                 |
|----------------|-----------------------------------|---------------------------------------------------------------------------------|
| Manual         | `labels_graph_process_summary.parquet` | Flattened data extracted from activity graphs.                                  |
| Scripted       | `process_lolbas_summary.parquet` | Matches LolBAS programs to processes.                                          |
| Scripted       | `process_mitre_summary.parquet`  | Matches processes to MITRE ATT&CK techniques.                                  |
| Scripted       | `sigma_labels_summary.parquet`   | Matches processes to Sigma rules.                                              |

_Note that all of these labels exist in the `process_uber_summary.parquet` file._

---

#### **Applications of ACME Datasets**
- **Cybersecurity Research**:
  - Enables the development of detection tools and machine learning models.
  - Facilitates the study of attacker behavior and defense mechanisms.
- **Education and Training**:
  - Provides realistic datasets for hands-on cybersecurity training.
- **Collaboration**:
  - Researchers can use ACME datasets to contribute to meaningful research and publish findings.


### Legal Notices
This data was created under work funded by a Laboratory Directed Research and Development project. This work was performed under the auspices of the U.S. Department of Energy by Lawrence Livermore National Laboratory under Contract **DE-AC52-07NA27344**, and is released under number **LLNL-MI-858068**.

[Privacy and Legal Notice](https://www.llnl.gov/disclaimer)

Learn about the Department of Energy's [Vulnerability Disclosure Programs](https://doe.responsibledisclosure.com/hc/en-us)