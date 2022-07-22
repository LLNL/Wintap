# How to Contribute
Wintap is an open source project. Our team welcomes contributions from collaborators in the form of raising issues as well as code contributions including hotfixes, code improvements, and new features.

Wintap is distributed under the terms of the MIT license. All new contributions must be made under this license.

If you identify a problem such as a bug or awkward or confusing code, or require a new feature, please feel free to start a thread on our issue tracker. Please first review the existing issues prior to avoid duplicate issues.

If you plan on contributing to Wintap, please review the issue tracker to check for threads related to your desired contribution. We recommend creating an issue prior to issuing a pull request if you are planning significant code changes or have questions.

# Contribution Workflow
These guidelines assume that the reader is familiar with the basics of collaborative development using git and GitHub. This section will walk through our preferred pull request workflow for contributing code to Wintap. The tl;dr guidance is:

Fork the LLNL Wintap repository
Create a descriptively named branch (feature/myfeature, iss/##, hotfix/bugname, etc) in your fork off of the develop branch
Commit code, following our guidelines
Create a pull request from your branch targeting the LLNL develop branch
# Forking Wintap
If you are not a Wintap developer at LLNL, you will not have permissions to push new branches to the repository. Even Wintap developers at LLNL will want to use forks for most contributions. This will create a clean copy of the repository that you own, and will allow for exploration and experimentation without muddying the history of the central repository.

If you intend to maintain a persistent fork of Wintap, it is a best practice to set the LLNL repository as the upstream remote in your fork.

$ git clone git@github.com:your_name/Wintap.git
$ cd Wintap
$ git remote add upstream git@github.com:LLNL/Wintap.git
This will allow you to incorporate changes to the master and develop branches as they evolve. For example, to your fork's develop branch perform the following commands:

$ git fetch upstream
$ git checkout develop
$ git pull upstream develop
$ git push origin develop
It is important to keep your develop branch up-to-date to reduce merge conflicts resulting from future PRs.

# Contribution Types
Most contributions will fit into one of the following categories, which by convention should be committed to branches with descriptive names. Here are some examples:

A new feature (feature/<feature-name>)
A bug or hotfix (hotfix/<bug-name> or hotfix/<issue-number>)
A response to a tracked issue (iss/<issue-number>)
A work in progress, not to be merged for some time (wip/<change-name>)
# Developing a new feature
New features should be based on the develop branch:

$ git checkout develop
$ git pull upstream develop
You can then create new local and remote branches on which to develop your feature.

$ git checkout -b feature/<feature-name>
$ git push --set-upstream origin feature/<feature-name>
Commit code changes to this branch.

Once your feature is complete, ensure that your remote fork is up-to-date and create a PR.

# Developing a hotfix
Firstly, please check to ensure that the bug you have found has not already been fixed in develop. If it has, we suggest that you temporarily swap to the develop branch.

If you have identified an unsolved bug, you can document the problem and create an issue. If you would like to solve the bug yourself, follow a similar protocol to feature development. First, ensure that your fork's develop branch is up-to-date.

$ git checkout develop
$ git pull upstream develop
You can then create new local and remote branches on which to write your bug fix.

$ git checkout -b hotfix/<bug-name>
$ git push --set-upstream origin hotfix/<bug-name>

Please update function and class documentation to reflect any changes as appropriate.

Once your are satisfied that the bug is fixed, ensure that your remote fork is up-to-date and create a PR.